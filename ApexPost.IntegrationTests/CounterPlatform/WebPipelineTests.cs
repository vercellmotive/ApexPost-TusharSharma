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
    public async Task GetHealth_AnyRequest_ReturnsTheSecurityHeaders()
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
    public async Task GetParcelStatus_WithoutAnApiKey_Returns401()
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
    public async Task GetParcelStatus_MoreRequestsThanTheBudget_Returns429()
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
        using var secondPartner = rateLimitFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        secondPartner.DefaultRequestHeaders.Add("X-Api-Key", "second-partner-test-key");
        secondPartner.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.50");
        var statuses = new List<HttpStatusCode>();

        // Act
        for (var requestNumber = 0; requestNumber < 65; requestNumber++)
        {
            using var response = await client.GetAsync("/integration/parcel-status");
            statuses.Add(response.StatusCode);
        }

        // The second partner has its own budget, so the first partner's 429s do not affect it.
        using var secondPartnerResponse = await secondPartner.GetAsync("/integration/parcel-status");

        // Assert
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Contains(HttpStatusCode.OK, statuses);
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
    public async Task GetHome_NotSignedIn_RedirectsToLogin()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task PostCounterVoid_ByAClerk_IsForbiddenAndAuditedWithoutTheReason()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync("ben@apexpost.com", "127.0.0.22");
        var counterPage = await client.GetAsync("/Counter");
        var voidToken = TokenFrom(await counterPage.Content.ReadAsStringAsync());
        var transactionId = Guid.NewGuid();

        // Act
        var response = await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("transactionId", transactionId.ToString()),
            new KeyValuePair<string, string>("reason", "Customer requested cancellation"),
            new KeyValuePair<string, string>("__RequestVerificationToken", voidToken)
        }));

        // Assert
        Assert.Equal(HttpStatusCode.OK, counterPage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var entries = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        var voidEntry = Assert.Single(entries, entry =>
            entry.Controller == "CounterController"
            && entry.Action == "Void"
            && entry.StaffId == "STF002"
            && entry.Parameters.Contains(transactionId.ToString(), StringComparison.Ordinal));
        Assert.Contains("\"transactionId\":", voidEntry.Parameters);
        Assert.DoesNotContain("Customer requested cancellation", voidEntry.Parameters);
    }

    [Fact]
    public async Task GetHealth_AnyRequest_ReturnsTheCorrelationHeader()
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
    public async Task BranchViews_SignedIn_RenderAndRestrictTheAuditReport()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync("alice@apexpost.com", "127.0.0.21");
        string[] branchViews = ["/Catalog", "/Counter", "/Reports", "/Shipping/ShippingLabel"];

        // Act
        var views = new Dictionary<string, HttpResponseMessage>();
        foreach (var path in branchViews)
        {
            views[path] = await client.GetAsync(path);
        }

        var auditorPage = await client.GetAsync("/Reports/Audit");

        // Assert
        Assert.All(views.Values, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var reportsHtml = await views["/Reports"].Content.ReadAsStringAsync();
        Assert.Contains("B001", reportsHtml);
        Assert.DoesNotContain("B002", reportsHtml);
        Assert.Equal(HttpStatusCode.Forbidden, auditorPage.StatusCode);
        var auditEntries = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        Assert.DoesNotContain(auditEntries, entry => entry.Action == "Login");
        Assert.DoesNotContain(auditEntries, entry => entry.Action == "Index");
    }

    [Fact]
    public async Task PostCounterCreate_HighValueMoneyOrderFails_DoesNotConsumeStock()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync(
            "alice@apexpost.com",
            TestClientAddress.For(nameof(PostCounterCreate_HighValueMoneyOrderFails_DoesNotConsumeStock)));
        var counterPage = await client.GetAsync("/Counter");
        var counterToken = TokenFrom(await counterPage.Content.ReadAsStringAsync());
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
                new KeyValuePair<string, string>("__RequestVerificationToken", counterToken)
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
        var administrator = await CreateAuthenticatedClientAsync("olive@apexpost.com");

        // Act
        var auditReport = await auditor.GetAsync("/Reports/Audit");
        var staffPage = await administrator.GetAsync("/Staff");
        var createStaffPage = await administrator.GetAsync("/Staff/Create");

        // Assert
        Assert.Equal(HttpStatusCode.OK, auditReport.StatusCode);
        Assert.Equal(HttpStatusCode.OK, staffPage.StatusCode);
        Assert.Equal(HttpStatusCode.OK, createStaffPage.StatusCode);
    }

    [Fact]
    public async Task CreateStaff_DuplicateEmail_RedisplaysFormWithError()
    {
        // Arrange
        var administrator = await CreateAuthenticatedClientAsync("olive@apexpost.com", "127.0.0.40");
        var formPage = await administrator.GetAsync("/Staff/Create");
        var token = TokenFrom(await formPage.Content.ReadAsStringAsync());

        // Act
        var response = await administrator.PostAsync("/Staff/Create", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Name", "Duplicate Account"),
            new KeyValuePair<string, string>("Email", "alice@apexpost.com"),
            new KeyValuePair<string, string>("Role", "CounterClerk"),
            new KeyValuePair<string, string>("HomeBranchCode", "B001"),
            new KeyValuePair<string, string>("Password", "A-long-enough-password"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        }));

        // Assert
        Assert.Equal(HttpStatusCode.OK, formPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("A staff member with this email already exists.", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostStockAdjust_BranchManager_UpdatesInventoryAndTheAuditRecord()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync("alice@apexpost.com", "127.0.0.41");
        var catalog = await client.GetAsync("/Catalog");
        var token = TokenFrom(await catalog.Content.ReadAsStringAsync());
        var before = _factory.Database.StockOf("B001", "SKU-001");

        // Act
        var response = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("productSku", "SKU-001"),
            new KeyValuePair<string, string>("quantityDelta", "2"),
            new KeyValuePair<string, string>("reasonCode", "Replenishment"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        }));

        // Assert
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(before + 2, _factory.Database.StockOf("B001", "SKU-001"));
        var auditEntry = (await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync()).Last(entry =>
            entry.Controller == "StockController"
            && entry.Action == "Adjust"
            && entry.StaffId == "STF001"
            && entry.Parameters.Contains("SKU-001", StringComparison.Ordinal));
        Assert.Contains("\"quantityDelta\":\"2\"", auditEntry.Parameters);
        Assert.Contains("\"reasonCode\":\"Replenishment\"", auditEntry.Parameters);
    }

    [Fact]
    public async Task PostCounterCreate_ValidSale_IsPersistedAndAudited()
    {
        // Arrange
        var client = await CreateAuthenticatedClientAsync("alice@apexpost.com", "127.0.0.41");
        var auditStore = _factory.Services.GetRequiredService<IAuditStore>();
        var initialQuantity = _factory.Database.StockOf("B001", "SKU-001");
        var counterPage = await client.GetAsync("/Counter");
        var token = TokenFrom(await counterPage.Content.ReadAsStringAsync());

        // Act
        var sale = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("CounterId", "C01"),
            new KeyValuePair<string, string>("RetailItems[0].ProductSku", "SKU-001"),
            new KeyValuePair<string, string>("RetailItems[0].Quantity", "1"),
            new KeyValuePair<string, string>("PaymentMethod", "Cash"),
            new KeyValuePair<string, string>("__RequestVerificationToken", token)
        }));

        // Assert
        Assert.True(initialQuantity > 0);
        Assert.Equal(HttpStatusCode.OK, counterPage.StatusCode);
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
        if (!token.Success)
        {
            throw new InvalidOperationException("The sign-in page has no antiforgery token.");
        }

        var tokenValue = token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value;
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Email", email),
            new KeyValuePair<string, string>("Password", DemoStaff.PasswordFor(email)),
            new KeyValuePair<string, string>("ReturnUrl", "/"),
            new KeyValuePair<string, string>("__RequestVerificationToken", tokenValue)
        }));
        if (login.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Signing in as {email} returned {login.StatusCode}, not a redirect.");
        }

        return client;
    }

    // Reads the antiforgery token a form needs. A page without one means the setup is broken, so it throws instead of
    // asserting.
    private static string TokenFrom(string html)
    {
        var token = FindAntiforgeryToken(html);
        if (!token.Success)
        {
            throw new InvalidOperationException("The page has no antiforgery token.");
        }

        return token.Groups[1].Success ? token.Groups[1].Value : token.Groups[2].Value;
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
