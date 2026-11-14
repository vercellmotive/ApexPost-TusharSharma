using System.Net;
using System.Text.Json;
using ApexPost.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.IntegrationTests.CounterPlatform;

public class WebPipelineTests : IClassFixture<ApexPostWebApplicationFactory>
{
    private static int _authenticatedClientIp = 39;
    private readonly ApexPostWebApplicationFactory _factory;

    public WebPipelineTests(ApexPostWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SecurityHeaders_ArePresent()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.True(response.Headers.Contains("Referrer-Policy"));
    }

    [Fact]
    public async Task IntegrationEndpoint_RequiresApiKey()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.25");
        var response = await client.GetAsync("/integration/parcel-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IntegrationEndpoint_WithApiKey_AllowsConfiguredWidgetOrigin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-integration-key");
        client.DefaultRequestHeaders.Add("Origin", "https://tracking-widget.apexpost.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.25");

        var response = await client.GetAsync("/integration/parcel-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "https://tracking-widget.apexpost.example",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task IntegrationEndpoint_BlockedIp_IsForbidden()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.1");
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-integration-key");

        var response = await client.GetAsync("/integration/parcel-status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PartnerRateLimit_Returns429AfterItsRequestBudgetIsExceeded()
    {
        using var rateLimitFactory = new PartnerRateLimitFactory();
        using var client = rateLimitFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Api-Key", "isolated-rate-limit-test-key");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.50");

        var statuses = new List<HttpStatusCode>();
        for (var requestNumber = 0; requestNumber < 65; requestNumber++)
        {
            using var response = await client.GetAsync("/integration/parcel-status");
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Contains(HttpStatusCode.OK, statuses);
        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Add("X-Api-Key", "second-partner-test-key");
        using var secondPartnerResponse = await client.GetAsync("/integration/parcel-status");
        Assert.Equal(HttpStatusCode.OK, secondPartnerResponse.StatusCode);
    }

    [Fact]
    public async Task LoginEndpoint_Returns429_AfterLimitExceeded()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.10");

        for (var i = 0; i < 3; i++)
        {
            await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
                new KeyValuePair<string, string>("Password", "wrong"),
                new KeyValuePair<string, string>("ReturnUrl", "/")
            }));
        }

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
            new KeyValuePair<string, string>("Password", "wrong"),
            new KeyValuePair<string, string>("ReturnUrl", "/")
        }));

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedAccess_RedirectsToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Clerk_IsForbidden_FromVoidAction()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.22");

        var loginPage = await client.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var tokenMatch = FindAntiforgeryToken(loginHtml);

        Assert.True(tokenMatch.Success, $"Login page did not contain an antiforgery token. Status: {loginPage.StatusCode}; body: {loginHtml}");

        var loginResponse = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", "ben@apexpost.com"),
            new KeyValuePair<string, string>("Password", "Apex-Ben-2026!"),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", tokenMatch.Groups[1].Value)
        }));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.True(loginResponse.Headers.Contains("Set-Cookie"), $"Login response did not issue cookies: {string.Join("; ", loginResponse.Headers.Select(header => $"{header.Key}={string.Join(",", header.Value)}"))}");

        var counterPage = await client.GetAsync("/Counter");
        Assert.Equal(HttpStatusCode.OK, counterPage.StatusCode);
        var counterHtml = await counterPage.Content.ReadAsStringAsync();
        var voidToken = FindAntiforgeryToken(counterHtml);

        Assert.True(voidToken.Success);

        var transactionId = Guid.NewGuid();
        var response = await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("transactionId", transactionId.ToString()),
            new KeyValuePair<string, string>("reason", "Customer requested cancellation"),
            new KeyValuePair<string, string>("__RequestVerificationToken", voidToken.Groups[1].Value)
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var auditStore = _factory.Services.GetRequiredService<IAuditStore>();
        var entries = await auditStore.GetAllAsync();
        var voidEntry = Assert.Single(entries, entry =>
            entry.Controller == "CounterController"
            && entry.Action == "Void"
            && entry.StaffId == "STF002"
            && entry.Parameters.Contains(transactionId.ToString(), StringComparison.Ordinal));
        Assert.Contains("\"transactionId\":", voidEntry.Parameters);
        Assert.DoesNotContain("Customer requested cancellation", voidEntry.Parameters);
    }

    [Fact]
    public async Task CorrelationHeader_IsReturned()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "classroom-test_123");
        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.Equal("classroom-test_123", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task AuthenticatedBranchViews_RenderAndAuditorReportIsRestricted()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.21");

        var loginPage = await client.GetAsync("/Account/Login");
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var token = FindAntiforgeryToken(loginHtml);
        Assert.True(token.Success);

        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
            new KeyValuePair<string, string>("Password", "Apex-Alice-2026!"),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Value)
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        foreach (var path in new[] { "/Catalog", "/Counter", "/Reports", "/Shipping/ShippingLabel" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (path == "/Reports")
            {
                var reportsHtml = await response.Content.ReadAsStringAsync();
                Assert.Contains("B001", reportsHtml);
                Assert.DoesNotContain("B002", reportsHtml);
            }
        }

        var auditorPage = await client.GetAsync("/Reports/Audit");
        Assert.Equal(HttpStatusCode.Forbidden, auditorPage.StatusCode);

        var auditStore = _factory.Services.GetRequiredService<IAuditStore>();
        var auditEntries = await auditStore.GetAllAsync();
        Assert.DoesNotContain(auditEntries, entry => entry.Action == "Login");
        Assert.DoesNotContain(auditEntries, entry => entry.Action == "Index");
    }

    [Fact]
    public async Task FailedHighValueMoneyOrder_DoesNotConsumeStock()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        var clientIp = Interlocked.Increment(ref _authenticatedClientIp);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"127.0.0.{clientIp}");
        var loginPage = await client.GetAsync("/Account/Login");
        var token = FindAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
        Assert.True(token.Success);

        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
            new KeyValuePair<string, string>("Password", "Apex-Alice-2026!"),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value)
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var counterPage = await client.GetAsync("/Counter");
        var counterToken = FindAntiforgeryToken(await counterPage.Content.ReadAsStringAsync());
        Assert.True(counterToken.Success);

        var stockBefore = _factory.Database.StockOf("B001", "SKU-001");

        var failure = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new[]
        {
                new KeyValuePair<string, string>("CounterId", "C01"),
                new KeyValuePair<string, string>("RetailItems[0].ProductSku", "SKU-001"),
                new KeyValuePair<string, string>("RetailItems[0].Quantity", "2"),
                new KeyValuePair<string, string>("MoneyOrders[0].Amount", "1500"),
                new KeyValuePair<string, string>("MoneyOrders[0].Sender", "Sender"),
                new KeyValuePair<string, string>("MoneyOrders[0].Recipient", "Recipient"),
                new KeyValuePair<string, string>("IncludeIdentityVerification", "true"),
                new KeyValuePair<string, string>("DocumentType", "Passport"),
                new KeyValuePair<string, string>("DocumentNumber", "AB123"),
                new KeyValuePair<string, string>("PaymentMethod", "Cash"),
                new KeyValuePair<string, string>("__RequestVerificationToken", counterToken.Groups[1].Success ? counterToken.Groups[1].Value : counterToken.Groups[2].Value)
            }));

        Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode);
        Assert.Contains("requires successful identity verification", await failure.Content.ReadAsStringAsync());
        Assert.Equal(stockBefore, _factory.Database.StockOf("B001", "SKU-001"));
    }

    [Fact]
    public async Task AuditorAndAdministratorCanAccessTheirRestrictedPages()
    {
        var auditor = await CreateAuthenticatedClientAsync("frank@apexpost.com");
        var auditReport = await auditor.GetAsync("/Reports/Audit");
        Assert.Equal(HttpStatusCode.OK, auditReport.StatusCode);

        var administrator = await CreateAuthenticatedClientAsync("olive@apexpost.com");
        var staffPage = await administrator.GetAsync("/Staff");
        Assert.Equal(HttpStatusCode.OK, staffPage.StatusCode);
        var createStaffPage = await administrator.GetAsync("/Staff/Create");
        Assert.Equal(HttpStatusCode.OK, createStaffPage.StatusCode);
    }

    [Fact]
    public async Task DuplicateStaffEmailRedisplaysFormWithValidationError()
    {
        var administrator = await CreateAuthenticatedClientAsync("olive@apexpost.com", "127.0.0.40");
        var formPage = await administrator.GetAsync("/Staff/Create");
        Assert.Equal(HttpStatusCode.OK, formPage.StatusCode);
        var token = FindAntiforgeryToken(await formPage.Content.ReadAsStringAsync());
        Assert.True(token.Success);

        var response = await administrator.PostAsync("/Staff/Create", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Name", "Duplicate Account"),
            new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
            new KeyValuePair<string, string>("Role", "CounterClerk"),
            new KeyValuePair<string, string>("HomeBranchCode", "B001"),
            new KeyValuePair<string, string>("Password", "A-long-enough-password"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value)
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("A staff member with this email already exists.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BranchManagerStockAdjustment_UpdatesInventoryAndAuditRecord()
    {
        var client = await CreateAuthenticatedClientAsync("alice@apexpost.com", "127.0.0.41");
        var catalog = await client.GetAsync("/Catalog");
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        var token = FindAntiforgeryToken(await catalog.Content.ReadAsStringAsync());
        Assert.True(token.Success);

        var before = _factory.Database.StockOf("B001", "SKU-001");
        var response = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("productSku", "SKU-001"),
            new KeyValuePair<string, string>("quantityDelta", "2"),
            new KeyValuePair<string, string>("reasonCode", "Replenishment"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value)
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(before + 2, _factory.Database.StockOf("B001", "SKU-001"));

        var auditStore = _factory.Services.GetRequiredService<IAuditStore>();
        var auditEntry = (await auditStore.GetAllAsync()).Last(entry =>
            entry.Controller == "StockController"
            && entry.Action == "Adjust"
            && entry.StaffId == "STF001"
            && entry.Parameters.Contains("SKU-001", StringComparison.Ordinal));
        Assert.Contains("\"quantityDelta\":\"2\"", auditEntry.Parameters);
        Assert.Contains("\"reasonCode\":\"Replenishment\"", auditEntry.Parameters);
    }

    [Fact]
    public async Task SuccessfulCounterSale_IsPersistedAndAudited()
    {
        var client = await CreateAuthenticatedClientAsync("alice@apexpost.com", "127.0.0.41");
        var auditStore = _factory.Services.GetRequiredService<IAuditStore>();
        var initialQuantity = _factory.Database.StockOf("B001", "SKU-001");
        Assert.True(initialQuantity > 0);

        var counterPage = await client.GetAsync("/Counter");
        Assert.Equal(HttpStatusCode.OK, counterPage.StatusCode);
        var token = FindAntiforgeryToken(await counterPage.Content.ReadAsStringAsync());
        Assert.True(token.Success);

        var sale = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("CounterId", "C01"),
            new KeyValuePair<string, string>("RetailItems[0].ProductSku", "SKU-001"),
            new KeyValuePair<string, string>("RetailItems[0].Quantity", "1"),
            new KeyValuePair<string, string>("PaymentMethod", "Cash"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value)
        }));

        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        var correlationId = Assert.Single(sale.Headers.GetValues("X-Correlation-Id"));
        Assert.Equal(initialQuantity - 1, _factory.Database.StockOf("B001", "SKU-001"));

        var saleAuditEntries = (await auditStore.GetAllAsync()).Where(entry =>
            entry.Controller == "CounterController"
            && entry.Action == "Create"
            && entry.StaffId == "STF001"
            && entry.StatusCode == StatusCodes.Status302Found
            && entry.CorrelationId == correlationId).ToList();
        var saleAudit = Assert.Single(saleAuditEntries);
        using var parameters = System.Text.Json.JsonDocument.Parse(saleAudit.Parameters);
        var transactionId = Guid.Parse(parameters.RootElement.GetProperty("transactionId").GetString()!);
        Assert.Equal(ApexPost.Domain.Enums.TransactionStatus.Paid, _factory.Database.TransactionById(transactionId).Status);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string clientIp = "127.0.0.30")
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp);
        var loginPage = await client.GetAsync("/Account/Login");
        var token = FindAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());
        Assert.True(token.Success);
        var tokenValue = token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value;
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", email),
            new KeyValuePair<string, string>("Password", $"Apex-{char.ToUpperInvariant(email[0])}{email[1..email.IndexOf('@')]}-2026!"),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", tokenValue)
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        return client;
    }

    private static System.Text.RegularExpressions.Match FindAntiforgeryToken(string html) =>
        System.Text.RegularExpressions.Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"|value=\"([^\"]+)\"[^>]*name=\"__RequestVerificationToken\"");

    private sealed class PartnerRateLimitFactory : ApexPostWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Integration:Partners:CourierA:ApiKey"] = "isolated-rate-limit-test-key",
                    ["Integration:Partners:CourierB:ApiKey"] = "second-partner-test-key",
                    ["Security:AllowedIps:0"] = "127.0.0.1",
                    ["Security:AllowedIps:1"] = "::1",
                    ["Security:AllowedIps:2"] = "127.0.0.50",
                    ["CourierTracking:SimulatedDelayMs"] = "0"
                });
            });
        }
    }
}
