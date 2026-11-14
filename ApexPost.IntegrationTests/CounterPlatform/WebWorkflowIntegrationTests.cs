extern alias web;
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
    private static int _clientNumber;
    private readonly ApexPostWebApplicationFactory _factory;

    public WebWorkflowIntegrationTests(ApexPostWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_UsesEachStaffMembersOwnPassword()
    {
        var client = CreateClient(_factory);

        Assert.Equal(HttpStatusCode.OK, await LoginAsync(client, "alice@apexpost.com", "Apex-Ben-2026!"));
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "alice@apexpost.com", "Apex-Alice-2026!"));
    }

    [Fact]
    public void SeedAndConfiguration_ContainHashesAndNoPlainPassword()
    {
        var staff = _factory.Database.AllStaff();

        Assert.All(staff, person => Assert.StartsWith("PBKDF2-SHA256$", person.PasswordHash));
        Assert.Equal(staff.Count, staff.Select(person => person.PasswordHash).Distinct().Count());
        var configuration = _factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
        Assert.Null(configuration["Authentication:DemoPassword"]);
    }

    [Fact]
    public async Task ClerkDeniedByPolicy_Gets403AndTheAttemptIsAudited()
    {
        var client = CreateClient(_factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "ben@apexpost.com", "Apex-Ben-2026!"));
        var token = await GetTokenAsync(client, "/Counter");

        var response = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["productSku"] = "SKU-001",
            ["quantityDelta"] = "1",
            ["reasonCode"] = "Replenishment",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var entries = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        Assert.Contains(entries, entry =>
            entry.StaffId == "STF002"
            && entry.StatusCode == 403
            && entry.Parameters.Contains("AccessDenied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConventionalRoutes_ReplaceTheOldAttributeRoutes()
    {
        var client = CreateClient(_factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "cara@apexpost.com", "Apex-Cara-2026!"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Shipping/ShippingLabel")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Counter/Transactions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Counter/ShippingLabel")).StatusCode);
    }

    [Fact]
    public async Task SameBranchPolicy_BlocksAnotherBranchsReports()
    {
        var client = CreateClient(_factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "grace@apexpost.com", "Apex-Grace-2026!"));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Reports?branchCode=B003")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Reports?branchCode=B001")).StatusCode);
    }

    [Fact]
    public async Task BlockedIpWithoutKey_GetsForbiddenBeforeTheKeyCheck()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.7");

        var response = await client.GetAsync("/integration/parcel-status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reports_DefaultToTheLatestDayWithSales()
    {
        var client = CreateClient(_factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "frank@apexpost.com", "Apex-Frank-2026!"));

        var html = await (await client.GetAsync("/Reports")).Content.ReadAsStringAsync();

        Assert.Contains("retail", html);
        Assert.DoesNotContain("No paid sales on this date.", html);
    }

    [Fact]
    public async Task ManagerCanVoidFromTheTransactionsScreen_AndTheDashboardShowsTheNotification()
    {
        var client = CreateClient(_factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "dom@apexpost.com", "Apex-Dom-2026!"));
        var page = await (await client.GetAsync("/Counter/Transactions")).Content.ReadAsStringAsync();
        Assert.Contains("/Counter/Void", page);
        Assert.Contains("/Counter/Refund", page);
        var transactionId = Regex.Match(page, "name=\"transactionId\" value=\"([^\"]+)\"").Groups[1].Value;

        var response = await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["transactionId"] = transactionId,
            ["reason"] = "Entered in error",
            ["__RequestVerificationToken"] = FindToken(page)
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var dashboard = await (await client.GetAsync("/")).Content.ReadAsStringAsync();
        Assert.Contains($"Transaction {transactionId} was voided or refunded", dashboard);
    }

    [Fact]
    public async Task AdministratorCanManageBranchesAndProducts()
    {
        var client = CreateClient(_factory, "127.0.0.60");
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "olive@apexpost.com", "Apex-Olive-2026!"));

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
        Assert.Equal(HttpStatusCode.Redirect, createBranch.StatusCode);

        var closeToken = await GetTokenAsync(client, "/Branches");
        var close = await client.PostAsync("/Branches/SetStatus/B090", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["isActive"] = "false",
            ["__RequestVerificationToken"] = closeToken
        }));
        Assert.Equal(HttpStatusCode.Redirect, close.StatusCode);
        var branch = _factory.Database.BranchByCode("B090");
        Assert.Equal(["C01", "C02"], branch!.CounterTerminals);
        Assert.False(branch.IsActive);

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
        Assert.Equal(HttpStatusCode.Redirect, createProduct.StatusCode);

        var catalogue = await (await client.GetAsync("/Catalog")).Content.ReadAsStringAsync();
        Assert.Contains("Test Envelope", catalogue);
        Assert.Equal(0, _factory.Database.StockOf("B090", "SKU-900"));
        var audit = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        Assert.Contains(audit, entry => entry.Parameters.Contains("\"businessAction\":\"ProductCreate\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedAuditWrite_DoesNotTurnACompletedActionInto500()
    {
        using var factory = new OverridingFactory(services =>
            services.AddSingleton<IAuditStore, ThrowingAuditStore>());
        var client = CreateClient(factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "hugo@apexpost.com", "Apex-Hugo-2026!"));
        var token = await GetTokenAsync(client, "/Catalog");

        var response = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["productSku"] = "SKU-001",
            ["quantityDelta"] = "1",
            ["reasonCode"] = "Replenishment",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task UnhandledFailure_IsLoggedAs500AndKeepsTheCorrelationId()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = new OverridingFactory(
            services => services.AddScoped(_ => DispatchProxy.Create<IScreenQueries, ThrowingProxy>()),
            logs);
        var client = CreateClient(factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "kara@apexpost.com", "Apex-Kara-2026!"));
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "failed-request-1");

        var response = await client.GetAsync("/Catalog");

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
    public async Task InvalidForm_IsRedisplayedByTheFilterWithItsChoices()
    {
        var client = CreateClient(_factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "ella@apexpost.com", "Apex-Ella-2026!"));
        var token = await GetTokenAsync(client, "/Services/MoneyOrder");

        var response = await client.PostAsync("/Services/MoneyOrder", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = "C01",
            ["Amount"] = "0",
            ["PaymentMethod"] = "Cash",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("The Sender field is required.", html);
        Assert.Matches("<option[^>]*value=\"C[0-9]+\"", html);
    }

    [Fact]
    public async Task NonFormEndpoint_ReturnsConsistentBadRequest()
    {
        var client = CreateClient(_factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "hugo@apexpost.com", "Apex-Hugo-2026!"));
        var token = await GetTokenAsync(client, "/Catalog");

        var response = await client.PostAsync("/Stock/Adjust", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["productSku"] = "SKU-001",
            ["quantityDelta"] = "not-a-number",
            ["reasonCode"] = "Replenishment",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StandaloneIdentityCheck_ShowsTheMaskedResultAndIsAudited()
    {
        var client = CreateClient(_factory);
        Assert.Equal(HttpStatusCode.Redirect, await LoginAsync(client, "noah@apexpost.com", "Apex-Noah-2026!"));
        var token = await GetTokenAsync(client, "/Services/IdentityVerification");

        var response = await client.PostAsync("/Services/IdentityVerification", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = _factory.Database.BranchByCode("B005").CounterTerminals[0],
            ["DocumentType"] = "Passport",
            ["DocumentNumber"] = "A123456789",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var page = await (await client.GetAsync("/Services/IdentityVerification")).Content.ReadAsStringAsync();
        Assert.Contains("******6789", page);
        Assert.DoesNotContain("A123456789", page);
        var audit = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        var entry = Assert.Single(audit, item => item.Action == "IdentityVerification" && item.StaffId == "STF014");
        Assert.DoesNotContain("A123456789", entry.Parameters);
    }

    [Fact]
    public async Task RateLimits_ComeFromConfiguration()
    {
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
        var second = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(form));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.True(second.Headers.Contains("Retry-After"));
    }

    private static HttpClient CreateClient(WebApplicationFactory<web::Program> factory, string? clientIp = null)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add(
            "X-Forwarded-For",
            clientIp ?? $"127.0.1.{Interlocked.Increment(ref _clientNumber)}");
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
