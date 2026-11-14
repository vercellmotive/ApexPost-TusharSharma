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
    private readonly ApexPostWebApplicationFactory _factory;

    public WebPipelineTests(ApexPostWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SecurityHeaders_ArePresent()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.True(response.Headers.Contains("Referrer-Policy"));
    }

    [Fact]
    public async Task IntegrationEndpoint_RequiresApiKey()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.25");

        // Act
        var response = await client.GetAsync("/integration/parcel-status");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IntegrationEndpoint_WithApiKey_AllowsConfiguredWidgetOrigin()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Api-Key", CounterTestSettings.IntegrationKey);
        client.DefaultRequestHeaders.Add("Origin", "https://tracking-widget.apexpost.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.25");

        // Act
        var response = await client.GetAsync("/integration/parcel-status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "https://tracking-widget.apexpost.example",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task IntegrationEndpoint_BlockedIp_IsForbidden()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.1");
        client.DefaultRequestHeaders.Add("X-Api-Key", CounterTestSettings.IntegrationKey);

        // Act
        var response = await client.GetAsync("/integration/parcel-status");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PartnerRateLimit_Returns429AfterItsRequestBudgetIsExceeded()
    {
        // Arrange
        using var rateLimitFactory = new PartnerRateLimitFactory();
        using var client = rateLimitFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Api-Key", "isolated-rate-limit-test-key");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.50");

        var statuses = new List<HttpStatusCode>();

        // Act
        for (var requestNumber = 0; requestNumber < 65; requestNumber++)
        {
            using var response = await client.GetAsync("/integration/parcel-status");
            statuses.Add(response.StatusCode);
        }

        // Assert
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Contains(HttpStatusCode.OK, statuses);

        // Arrange (next step)
        client.DefaultRequestHeaders.Remove("X-Api-Key");
        client.DefaultRequestHeaders.Add("X-Api-Key", "second-partner-test-key");

        // Act
        using var secondPartnerResponse = await client.GetAsync("/integration/parcel-status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, secondPartnerResponse.StatusCode);
    }

    [Fact]
    public async Task LoginEndpoint_Returns429_AfterLimitExceeded()
    {
        // Arrange
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

        // Act
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
            new KeyValuePair<string, string>("Password", "wrong"),
            new KeyValuePair<string, string>("ReturnUrl", "/")
        }));

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedAccess_RedirectsToLogin()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Clerk_IsForbidden_FromVoidAction()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.22");

        // Act
        var loginPage = await client.GetAsync("/Account/Login");

        // Assert
        var loginHtml = await loginPage.Content.ReadAsStringAsync();

        // Act
        var tokenMatch = FindAntiforgeryToken(loginHtml);

        // Assert
        Assert.True(tokenMatch.Success, $"Login page did not contain an antiforgery token. Status: {loginPage.StatusCode}; body: {loginHtml}");

        // Act
        var loginResponse = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", "ben@apexpost.com"),
            new KeyValuePair<string, string>("Password", DemoStaff.PasswordFor("ben@apexpost.com")),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", tokenMatch.Groups[1].Value)
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.True(loginResponse.Headers.Contains("Set-Cookie"), $"Login response did not issue cookies: {string.Join("; ", loginResponse.Headers.Select(header => $"{header.Key}={string.Join(",", header.Value)}"))}");

        // Act
        var counterPage = await client.GetAsync("/Counter");

        // Assert
        Assert.Equal(HttpStatusCode.OK, counterPage.StatusCode);

        var counterHtml = await counterPage.Content.ReadAsStringAsync();

        // Act
        var voidToken = FindAntiforgeryToken(counterHtml);

        // Assert
        Assert.True(voidToken.Success);

        // Arrange (next step)
        var transactionId = Guid.NewGuid();

        // Act
        var response = await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("transactionId", transactionId.ToString()),
            new KeyValuePair<string, string>("reason", "Customer requested cancellation"),
            new KeyValuePair<string, string>("__RequestVerificationToken", voidToken.Groups[1].Value)
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Arrange (next step)
        var auditStore = _factory.Services.GetRequiredService<IAuditStore>();

        // Act
        var entries = await auditStore.GetAllAsync();

        // Assert
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
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "classroom-test_123");

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        Assert.Equal("classroom-test_123", response.Headers.GetValues("X-Correlation-Id").Single());
    }

    [Fact]
    public async Task AuthenticatedBranchViews_RenderAndAuditorReportIsRestricted()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.21");

        // Act
        var loginPage = await client.GetAsync("/Account/Login");

        // Assert
        var loginHtml = await loginPage.Content.ReadAsStringAsync();

        // Act
        var token = FindAntiforgeryToken(loginHtml);

        // Assert
        Assert.True(token.Success);

        // Act
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
            new KeyValuePair<string, string>("Password", DemoStaff.PasswordFor("alice@apexpost.com")),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Value)
        }));

        // Assert
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

        // Act
        var auditorPage = await client.GetAsync("/Reports/Audit");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, auditorPage.StatusCode);

        // Arrange (next step)
        var auditStore = _factory.Services.GetRequiredService<IAuditStore>();

        // Act
        var auditEntries = await auditStore.GetAllAsync();

        // Assert
        Assert.DoesNotContain(auditEntries, entry => entry.Action == "Login");
        Assert.DoesNotContain(auditEntries, entry => entry.Action == "Index");
    }

    [Fact]
    public async Task FailedHighValueMoneyOrder_DoesNotConsumeStock()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", TestClientAddress.For(nameof(FailedHighValueMoneyOrder_DoesNotConsumeStock)));

        // Act
        var loginPage = await client.GetAsync("/Account/Login");

        // Assert
        var token = FindAntiforgeryToken(await loginPage.Content.ReadAsStringAsync());

        Assert.True(token.Success);

        // Act
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
            new KeyValuePair<string, string>("Password", DemoStaff.PasswordFor("alice@apexpost.com")),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value)
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        // Act
        var counterPage = await client.GetAsync("/Counter");

        // Assert
        var counterToken = FindAntiforgeryToken(await counterPage.Content.ReadAsStringAsync());

        Assert.True(counterToken.Success);

        // Arrange (next step)
        var stockBefore = _factory.Database.StockOf("B001", "SKU-001");

        // Act
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

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, failure.StatusCode);
        Assert.Contains("requires successful identity verification", await failure.Content.ReadAsStringAsync());
        Assert.Equal(stockBefore, _factory.Database.StockOf("B001", "SKU-001"));
    }

    [Fact]
    public async Task RestrictedPages_AuditorAndAdministrator_CanAccessTheirOwn()
    {
        // Arrange
        var auditor = await CreateAuthenticatedClientAsync("frank@apexpost.com");

        // Act
        var auditReport = await auditor.GetAsync("/Reports/Audit");

        // Assert
        Assert.Equal(HttpStatusCode.OK, auditReport.StatusCode);

        // Arrange (next step)
        var administrator = await CreateAuthenticatedClientAsync("olive@apexpost.com");

        // Act
        var staffPage = await administrator.GetAsync("/Staff");

        // Assert
        Assert.Equal(HttpStatusCode.OK, staffPage.StatusCode);

        // Act
        var createStaffPage = await administrator.GetAsync("/Staff/Create");

        // Assert
        Assert.Equal(HttpStatusCode.OK, createStaffPage.StatusCode);
    }

    [Fact]
    public async Task CreateStaff_DuplicateEmail_RedisplaysFormWithError()
    {
        // Arrange
        var administrator = await CreateAuthenticatedClientAsync("olive@apexpost.com", "127.0.0.40");

        // Act
        var formPage = await administrator.GetAsync("/Staff/Create");

        // Assert
        Assert.Equal(HttpStatusCode.OK, formPage.StatusCode);

        var token = FindAntiforgeryToken(await formPage.Content.ReadAsStringAsync());

        Assert.True(token.Success);

        // Act
        var response = await administrator.PostAsync("/Staff/Create", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Name", "Duplicate Account"),
            new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
            new KeyValuePair<string, string>("Role", "CounterClerk"),
            new KeyValuePair<string, string>("HomeBranchCode", "B001"),
            new KeyValuePair<string, string>("Password", "A-long-enough-password"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value)
        }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("A staff member with this email already exists.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task BranchManagerStockAdjustment_UpdatesInventoryAndAuditRecord()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync("alice@apexpost.com", "127.0.0.41");

        // Act
        var catalog = await client.GetAsync("/Catalog");

        // Assert
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);

        var token = FindAntiforgeryToken(await catalog.Content.ReadAsStringAsync());

        Assert.True(token.Success);

        // Arrange (next step)
        var before = _factory.Database.StockOf("B001", "SKU-001");

        // Act
        var response = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("productSku", "SKU-001"),
            new KeyValuePair<string, string>("quantityDelta", "2"),
            new KeyValuePair<string, string>("reasonCode", "Replenishment"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value)
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(before + 2, _factory.Database.StockOf("B001", "SKU-001"));

        // Arrange (next step)
        var auditStore = _factory.Services.GetRequiredService<IAuditStore>();

        // Act
        var auditEntry = (await auditStore.GetAllAsync()).Last(entry =>
            entry.Controller == "StockController"
            && entry.Action == "Adjust"
            && entry.StaffId == "STF001"
            && entry.Parameters.Contains("SKU-001", StringComparison.Ordinal));

        // Assert
        Assert.Contains("\"quantityDelta\":\"2\"", auditEntry.Parameters);
        Assert.Contains("\"reasonCode\":\"Replenishment\"", auditEntry.Parameters);
    }

    [Fact]
    public async Task SuccessfulCounterSale_IsPersistedAndAudited()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync("alice@apexpost.com", "127.0.0.41");
        var auditStore = _factory.Services.GetRequiredService<IAuditStore>();

        // Act
        var initialQuantity = _factory.Database.StockOf("B001", "SKU-001");

        // Assert
        Assert.True(initialQuantity > 0);

        // Act
        var counterPage = await client.GetAsync("/Counter");

        // Assert
        Assert.Equal(HttpStatusCode.OK, counterPage.StatusCode);

        var token = FindAntiforgeryToken(await counterPage.Content.ReadAsStringAsync());

        Assert.True(token.Success);

        // Act
        var sale = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("CounterId", "C01"),
            new KeyValuePair<string, string>("RetailItems[0].ProductSku", "SKU-001"),
            new KeyValuePair<string, string>("RetailItems[0].Quantity", "1"),
            new KeyValuePair<string, string>("PaymentMethod", "Cash"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value)
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        var correlationId = Assert.Single(sale.Headers.GetValues("X-Correlation-Id"));
        Assert.Equal(initialQuantity - 1, _factory.Database.StockOf("B001", "SKU-001"));

        // Act
        var saleAuditEntries = (await auditStore.GetAllAsync()).Where(entry =>
            entry.Controller == "CounterController"
            && entry.Action == "Create"
            && entry.StaffId == "STF001"
            && entry.StatusCode == StatusCodes.Status302Found
            && entry.CorrelationId == correlationId).ToList();

        // Assert
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
            new KeyValuePair<string, string>("Password", DemoStaff.PasswordFor(email)),
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
