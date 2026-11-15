extern alias web;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using ApexPost.Application.Interfaces;
using ApexPost.Application.Queries;
using ApexPost.Domain.Entities;
using ApexPost.Domain.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.IntegrationTests.CounterPlatform;

public sealed class WebWorkflowIntegrationTests : IClassFixture<ApexPostWebApplicationFactory>
{
    private readonly ApexPostWebApplicationFactory _factory;

    public WebWorkflowIntegrationTests(ApexPostWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostLogin_AnotherStaffMembersPassword_IsRefused()
    {
        // Arrange
        var client = CreateClient(_factory);

        // Act
        var withBensPassword = await LoginAsync(client, "alice@apexpost.com", DemoStaff.PasswordFor("ben@apexpost.com"));
        var withOwnPassword = await LoginAsync(client, "alice@apexpost.com", DemoStaff.PasswordFor("alice@apexpost.com"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, withBensPassword);
        Assert.Equal(HttpStatusCode.Redirect, withOwnPassword);
    }

    [Fact]
    public void SeedAndConfiguration_StaffPasswords_AreHashesNotPlainText()
    {
        // Act
        var staff = _factory.Database.AllStaff();
        var configuration = _factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();

        // Assert
        Assert.All(staff, person => Assert.StartsWith("PBKDF2-SHA256$", person.PasswordHash));
        Assert.Equal(staff.Count, staff.Select(person => person.PasswordHash).Distinct().Count());
        Assert.Null(configuration["Authentication:DemoPassword"]);
    }

    [Fact]
    public async Task PostStockAdjust_ByAClerk_Returns403AndIsAudited()
    {
        // Arrange
        var client = CreateClient(_factory);
        await SignInAsync(client, "ben@apexpost.com");
        var token = await GetTokenAsync(client, "/Counter");

        // Act
        var response = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["productSku"] = "SKU-001",
            ["quantityDelta"] = "1",
            ["reasonCode"] = "Replenishment",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var entries = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        Assert.Contains(entries, entry =>
            entry.StaffId == "STF002"
            && entry.StatusCode == 403
            && entry.Parameters.Contains("AccessDenied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Routing_OldAttributeRoutes_AreReplacedByConventionalRoutes()
    {
        // Arrange
        var client = CreateClient(_factory);
        await SignInAsync(client, "cara@apexpost.com");

        // Act
        var shippingLabel = await client.GetAsync("/Shipping/ShippingLabel");
        var transactions = await client.GetAsync("/Counter/Transactions");
        var oldShippingLabelRoute = await client.GetAsync("/Counter/ShippingLabel");

        // Assert
        Assert.Equal(HttpStatusCode.OK, shippingLabel.StatusCode);
        Assert.Equal(HttpStatusCode.OK, transactions.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldShippingLabelRoute.StatusCode);
    }

    [Fact]
    public async Task GetReports_AnotherBranch_IsForbidden()
    {
        // Arrange
        var client = CreateClient(_factory);
        await SignInAsync(client, "grace@apexpost.com");

        // Act
        var ownBranch = await client.GetAsync("/Reports?branchCode=B003");
        var otherBranch = await client.GetAsync("/Reports?branchCode=B001");

        // Assert
        Assert.Equal(HttpStatusCode.OK, ownBranch.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherBranch.StatusCode);
    }

    [Fact]
    public async Task GetParcelStatus_BlockedIpWithoutAKey_Returns403BeforeTheKeyCheck()
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        // Act
        var response = await client.GetAsync("/integration/parcel-status");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReports_NoDateGiven_ShowsTheLatestDayWithSales()
    {
        // Arrange
        var client = CreateClient(_factory);
        await SignInAsync(client, "frank@apexpost.com");

        // Act
        var html = await (await client.GetAsync("/Reports")).Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("retail", html);
        Assert.DoesNotContain("No paid sales on this date.", html);
    }

    [Fact]
    public async Task PostCounterVoid_ManagerFromTheTransactionsScreen_VoidsAndTheDashboardShowsIt()
    {
        // Arrange
        var client = CreateClient(_factory);
        await SignInAsync(client, "dom@apexpost.com");
        var page = await (await client.GetAsync("/Counter/Transactions")).Content.ReadAsStringAsync();
        var transactionId = Regex.Match(page, "name=\"transactionId\" value=\"([^\"]+)\"").Groups[1].Value;

        // Act
        var response = await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["transactionId"] = transactionId,
            ["reason"] = "Entered in error",
            ["__RequestVerificationToken"] = FindToken(page)
        }));
        var dashboard = await (await client.GetAsync("/")).Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("/Counter/Void", page);
        Assert.Contains("/Counter/Refund", page);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains($"Transaction {transactionId} was voided or refunded", dashboard);
    }

    [Fact]
    public async Task Administrator_BranchesAndProducts_CanManageBoth()
    {
        // Arrange
        var client = CreateClient(_factory, "127.0.0.60");
        await SignInAsync(client, "olive@apexpost.com");

        // Act
        // The administrator creates a branch, closes it, then adds a product.
        var branchToken = await GetTokenAsync(client, "/Branches/Create");
        var createBranch = await client.PostAsync("/Branches/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Code"] = "B090",
            ["Name"] = "Test Branch",
            ["Region"] = "NSW",
            ["OpenTime"] = "09:00",
            ["CloseTime"] = "17:00",
            ["TimeZoneId"] = "Australia/Sydney",
            ["CounterTerminals"] = "C01, C02",
            ["__RequestVerificationToken"] = branchToken
        }));
        var closeToken = await GetTokenAsync(client, "/Branches");
        var close = await client.PostAsync("/Branches/SetStatus/B090", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["isActive"] = "false",
            ["__RequestVerificationToken"] = closeToken
        }));
        var productToken = await GetTokenAsync(client, "/Products/Create");
        var createProduct = await client.PostAsync("/Products/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Sku"] = "SKU-900",
            ["Name"] = "Test Envelope",
            ["Category"] = "Stationery",
            ["UnitPrice"] = "2.50",
            ["TaxCategory"] = "StandardGst",
            ["__RequestVerificationToken"] = productToken
        }));
        var catalogue = await (await client.GetAsync("/Catalog")).Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, createBranch.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, close.StatusCode);
        var branch = _factory.Database.BranchByCode("B090");
        Assert.Equal(["C01", "C02"], branch!.CounterTerminals);
        Assert.False(branch.IsActive);
        Assert.Equal(HttpStatusCode.Redirect, createProduct.StatusCode);
        Assert.Contains("Test Envelope", catalogue);
        Assert.Equal(0, _factory.Database.StockOf("B090", "SKU-900"));
        var audit = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        Assert.Contains(audit, entry => entry.Parameters.Contains("\"businessAction\":\"ProductCreate\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostStockAdjust_AuditWriteFails_StillCompletesWithoutA500()
    {
        // Arrange
        using var factory = new OverridingFactory(services =>
            services.AddSingleton<IAuditStore, ThrowingAuditStore>());
        var client = CreateClient(factory);
        await SignInAsync(client, "hugo@apexpost.com");
        var token = await GetTokenAsync(client, "/Catalog");

        // Act
        var response = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["productSku"] = "SKU-001",
            ["quantityDelta"] = "1",
            ["reasonCode"] = "Replenishment",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task GetCatalog_UnhandledFailure_IsLoggedAs500WithTheCorrelationId()
    {
        // Arrange
        var logs = new CapturingLoggerProvider();
        using var factory = new OverridingFactory(
            services => services.AddScoped(_ => DispatchProxy.Create<IScreenQueries, ThrowingProxy>()),
            logs);
        var client = CreateClient(factory);
        await SignInAsync(client, "kara@apexpost.com");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "failed-request-1");

        // Act
        var response = await client.GetAsync("/Catalog");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("failed-request-1", Assert.Single(response.Headers.GetValues("X-Correlation-Id")));
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"traceId\":\"failed-request-1\"", body);
        Assert.DoesNotContain("Simulated catalogue failure", body);
        Assert.Contains(logs.Messages, message =>
            message.StartsWith("HTTP GET /Catalog responded 500", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PostMoneyOrder_InvalidForm_IsRedisplayedWithItsChoices()
    {
        // Arrange
        var client = CreateClient(_factory);
        await SignInAsync(client, "ella@apexpost.com");
        var token = await GetTokenAsync(client, "/Services/MoneyOrder");

        // Act
        var response = await client.PostAsync("/Services/MoneyOrder", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = "C01",
            ["Amount"] = "0",
            ["PaymentMethod"] = "Cash",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("The Sender field is required.", html);
        Assert.Matches("<option[^>]*value=\"C[0-9]+\"", html);
    }

    [Fact]
    public async Task PostStockAdjust_InvalidNonFormRequest_ReturnsAConsistentBadRequest()
    {
        // Arrange
        var client = CreateClient(_factory);
        await SignInAsync(client, "hugo@apexpost.com");
        var token = await GetTokenAsync(client, "/Catalog");

        // Act
        var response = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["productSku"] = "SKU-001",
            ["quantityDelta"] = "not-a-number",
            ["reasonCode"] = "Replenishment",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostIdentityVerification_ValidDocument_ShowsTheMaskedResultAndIsAudited()
    {
        // Arrange
        var client = CreateClient(_factory);
        await SignInAsync(client, "noah@apexpost.com");
        var token = await GetTokenAsync(client, "/Services/IdentityVerification");

        // Act
        var response = await client.PostAsync("/Services/IdentityVerification", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = _factory.Database.BranchByCode("B005").CounterTerminals[0],
            ["DocumentType"] = "Passport",
            ["DocumentNumber"] = "A123456789",
            ["__RequestVerificationToken"] = token
        }));
        var page = await (await client.GetAsync("/Services/IdentityVerification")).Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("******6789", page);
        Assert.DoesNotContain("A123456789", page);
        var audit = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        var entry = Assert.Single(audit, item => item.Action == "IdentityVerification" && item.StaffId == "STF014");
        Assert.DoesNotContain("A123456789", entry.Parameters);
    }

    [Fact]
    public async Task PostLogin_OverTheConfiguredLimit_Returns429WithRetryAfter()
    {
        // Arrange
        using var factory = new ConfiguredLoginLimitFactory();
        var client = CreateClient(factory);
        var token = await GetTokenAsync(client, "/Account/Login");
        var form = new Dictionary<string, string>
        {
            ["Email"] = "nobody@apexpost.com",
            ["Password"] = "wrong",
            ["__RequestVerificationToken"] = token
        };

        var first = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));

        // Act
        var second = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));

        // Assert
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.Contains("Retry-After"));
    }

    private static HttpClient CreateClient(WebApplicationFactory<web::Program> factory, string? clientIp = null, [CallerMemberName] string test = "")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            clientIp ?? TestClientAddress.For(test));
        return client;
    }

    private static async Task<HttpStatusCode> LoginAsync(HttpClient client, string email, string password)
    {
        var token = await GetTokenAsync(client, "/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
        return response.StatusCode;
    }

    // Signs in during Arrange. A failed sign-in means the setup is broken, not the behaviour under test, so it throws
    // instead of asserting.
    private static async Task SignInAsync(HttpClient client, string email)
    {
        var status = await LoginAsync(client, email, DemoStaff.PasswordFor(email));
        if (status != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Signing in as {email} returned {status}, not a redirect.");
        }
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string path) =>
        FindToken(await (await client.GetAsync(path)).Content.ReadAsStringAsync());

    private static string FindToken(string html) =>
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private sealed class OverridingFactory(
        Action<IServiceCollection> overrides,
        ILoggerProvider? loggerProvider = null) : ApexPostWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(overrides);
            if (loggerProvider is not null)
            {
                builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider));
            }
        }
    }

    private sealed class ConfiguredLoginLimitFactory : ApexPostWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimiting:Login:PermitLimit"] = "1"
                }));
        }
    }

    private sealed class ThrowingAuditStore : IAuditStore
    {
        public Task AppendAsync(AuditEntry entry, CancellationToken ct = default) =>
            throw new IOException("Simulated audit disk failure.");

        public Task<IReadOnlyList<AuditEntry>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditEntry>>([]);
    }

    public class ThrowingProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Simulated catalogue failure.");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider, ILogger
    {
        public ConcurrentBag<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));

        public void Dispose()
        {
        }
    }
}
