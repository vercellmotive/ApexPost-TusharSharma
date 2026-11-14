extern alias web;
using System.Runtime.CompilerServices;
using System.Net;
using System.Text.RegularExpressions;
using ApexPost.Application.Interfaces;
using ApexPost.Application.Queries;
using ApexPost.Domain.Common;
using ApexPost.Domain.Enums;
using ApexPost.Web.Configuration;
using ApexPost.Web.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ApexPost.Testing.CounterPlatform;

namespace ApexPost.IntegrationTests.CounterPlatform;

public sealed class ErrorMappingAndOverPostingTests : IClassFixture<ApexPostWebApplicationFactory>
{
    private const string AllowedPartnerIp = "127.0.0.25";
    private readonly ApexPostWebApplicationFactory _factory;

    public ErrorMappingAndOverPostingTests(ApexPostWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public static TheoryData<Exception, int> Failures => new()
    {
        { new ConcurrencyConflictException("Changed by someone else."), StatusCodes.Status409Conflict },
        { new DuplicateValueException("Already exists."), StatusCodes.Status409Conflict },
        { new KeyNotFoundException("Missing."), StatusCodes.Status404NotFound },
        { new DomainValidationException("Broke a rule."), StatusCodes.Status400BadRequest },
        { new InvalidOperationException("Secret internal detail."), StatusCodes.Status500InternalServerError }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Exceptions_MapToProblemDetailsWithTheirStatus(Exception failure, int expectedStatus)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(_ => throw failure, NullLogger<GlobalExceptionMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        // Arrange (next step)
        context.Response.Body.Position = 0;

        // Act
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        // Assert
        Assert.Contains($"\"status\":{expectedStatus}", body);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("Secret internal detail", body);
    }

    [Fact]
    public async Task ConcurrencyConflictDuringSale_Returns409ProblemDetails()
    {
        // Arrange
        using var factory = new ConflictOnSaveFactory();
        var client = CreateClient(factory);
        await LoginAsync(client, "ben@apexpost.com", DemoStaff.PasswordFor("ben@apexpost.com"));
        var token = await GetTokenAsync(client, "/Counter");

        // Act
        var response = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = "C01",
            ["RetailItems[0].ProductSku"] = "SKU-001",
            ["RetailItems[0].Quantity"] = "1",
            ["PaymentMethod"] = "Cash",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // Act
        var audit = await factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();

        // Assert
        Assert.Contains(audit, entry => entry.Action == "Create" && entry.StatusCode == StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task OverPostedSaleFields_HaveNoEffect()
    {
        // Arrange
        var client = CreateClient(_factory);
        await LoginAsync(client, "cara@apexpost.com", DemoStaff.PasswordFor("cara@apexpost.com"));
        var token = await GetTokenAsync(client, "/Counter");
        var postedId = Guid.NewGuid();

        // Act
        var response = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = "C01",
            ["RetailItems[0].ProductSku"] = "SKU-002",
            ["RetailItems[0].Quantity"] = "1",
            ["PaymentMethod"] = "Cash",
            ["Id"] = postedId.ToString(),
            ["Status"] = "Voided",
            ["ClerkStaffId"] = "STF001",
            ["BranchCode"] = "B002",
            ["CreatedBy"] = "attacker",
            ["CreatedAtUtc"] = "2000-01-01",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Act
        var created = _factory.Database.AllTransactions().Last();

        // Assert
        Assert.NotEqual(postedId, created.Id);
        Assert.Equal(TransactionStatus.Paid, created.Status);
        Assert.Equal("STF003", created.ClerkStaffId);
        Assert.Equal("B001", created.BranchCode.Value);
    }

    [Fact]
    public async Task OverPostedStaffFields_HaveNoEffect()
    {
        // Arrange
        var client = CreateClient(_factory, "127.0.0.61");
        await LoginAsync(client, "olive@apexpost.com", DemoStaff.PasswordFor("olive@apexpost.com"));
        var token = await GetTokenAsync(client, "/Staff/Edit/STF016");
        var before = _factory.Database.StaffById("STF016");

        // Act
        var response = await client.PostAsync("/Staff/Edit/STF016", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Name"] = "Parker Quinn",
            ["Email"] = before.Email,
            ["Role"] = before.Role.ToString(),
            ["HomeBranchCode"] = before.HomeBranchCode.Value,
            ["Id"] = "STF999",
            ["IsActive"] = "false",
            ["PasswordHash"] = "attacker-hash",
            ["CreatedBy"] = "attacker",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Act
        var after = _factory.Database.StaffById("STF016");

        // Assert
        Assert.True(after.IsActive);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.DoesNotContain(_factory.Database.AllStaff(), person => person.Id == "STF999");
    }

    [Fact]
    public async Task PriceRevision_IsSetBasedAndChangesOnlyItsCategory()
    {
        // Arrange
        var client = CreateClient(_factory, "127.0.0.61");
        await LoginAsync(client, "olive@apexpost.com", DemoStaff.PasswordFor("olive@apexpost.com"));
        var token = await GetTokenAsync(client, "/Products");
        var packagingBefore = _factory.Database.ProductBySku("SKU-001").UnitPrice;

        // Act
        var response = await client.PostAsync("/Products/RevisePrices", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Category"] = "Stationery",
            ["PercentChange"] = "10",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(packagingBefore, _factory.Database.ProductBySku("SKU-001").UnitPrice);
    }

    [Fact]
    public async Task ReportTotals_MatchThePersistedSale()
    {
        // Arrange
        var client = CreateClient(_factory);
        await LoginAsync(client, "alice@apexpost.com", DemoStaff.PasswordFor("alice@apexpost.com"));
        var token = await GetTokenAsync(client, "/Services/MoneyOrder");
        var today = DateOnly.FromDateTime(_factory.Clock.GetUtcNow().UtcDateTime);
        using var scope = _factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportQueries>();
        var before = (await reports.GetDailyBranchSalesAsync("B001", today)).SingleOrDefault()?.ServicesTotal ?? 0m;

        // Act
        var response = await client.PostAsync("/Services/MoneyOrder", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = "C01",
            ["Amount"] = "200",
            ["Sender"] = "Sender",
            ["Recipient"] = "Recipient",
            ["PaymentMethod"] = "Cash",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        // Alice is a manager at B001, so the sale goes through and only the fee counts as revenue.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        // Act
        var after = (await reports.GetDailyBranchSalesAsync("B001", today)).Single().ServicesTotal;

        // Assert
        Assert.Equal(before + 10m, after);
    }

    [Fact]
    public async Task ReversalAndStockErrors_ReturnProblemDetailsAndAreAudited()
    {
        // Arrange
        var client = CreateClient(_factory);
        await LoginAsync(client, "alice@apexpost.com", DemoStaff.PasswordFor("alice@apexpost.com"));
        var token = await GetTokenAsync(client, "/Counter");

        var unknownId = Guid.NewGuid();

        // Act and assert
        await AssertProblemAsync(await client.PostAsync("/Counter/Void", ReversalForm(unknownId, token)), HttpStatusCode.NotFound);
        await AssertProblemAsync(await client.PostAsync("/Counter/Refund", ReversalForm(unknownId, token)), HttpStatusCode.NotFound);
        await AssertProblemAsync(await client.PostAsync("/Stock/Adjust", StockForm("SKU-001", -1000000, token)), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await client.PostAsync("/Stock/Adjust", StockForm("SKU-404", 1, token)), HttpStatusCode.NotFound);

        // Arrange (next step)
        var known = _factory.Database.AllTransactions().Select(transaction => transaction.Id).ToHashSet();

        // Act
        var sale = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = "C01",
            ["RetailItems[0].ProductSku"] = "SKU-001",
            ["RetailItems[0].Quantity"] = "1",
            ["PaymentMethod"] = "Cash",
            ["__RequestVerificationToken"] = token
        }));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);

        // Arrange (next step)
        var saleId = _factory.Database.AllTransactions().Single(transaction => !known.Contains(transaction.Id)).Id;

        // Act and assert
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Counter/Void", ReversalForm(saleId, token))).StatusCode);
        await AssertProblemAsync(await client.PostAsync("/Counter/Void", ReversalForm(saleId, token)), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await client.PostAsync("/Counter/Refund", ReversalForm(saleId, token)), HttpStatusCode.BadRequest);

        // Act
        var audit = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();

        // Assert
        Assert.Contains(audit, entry => entry.Action == "Void" && entry.StatusCode == StatusCodes.Status404NotFound);
        Assert.Contains(audit, entry => entry.Action == "Void" && entry.StatusCode == StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task AdministratorRequestsForUnknownRecords_Return404ProblemDetails()
    {
        // Arrange
        var client = CreateClient(_factory, "127.0.0.61");
        await LoginAsync(client, "olive@apexpost.com", DemoStaff.PasswordFor("olive@apexpost.com"));
        var token = await GetTokenAsync(client, "/Staff");
        var form = new Dictionary<string, string>
        {
            ["IsActive"] = "false",
            ["__RequestVerificationToken"] = token
        };

        // Act and assert
        await AssertProblemAsync(await client.PostAsync("/Staff/Deactivate/STF999", new FormUrlEncodedContent(form)), HttpStatusCode.NotFound);
        await AssertProblemAsync(await client.PostAsync("/Branches/SetStatus/B999", new FormUrlEncodedContent(form)), HttpStatusCode.NotFound);
        await AssertProblemAsync(await client.GetAsync("/Staff/Edit/STF999"), HttpStatusCode.NotFound);
        await AssertProblemAsync(await client.GetAsync("/Branches/Edit/B999"), HttpStatusCode.NotFound);
        await AssertProblemAsync(await client.GetAsync("/Products/Edit/SKU-404"), HttpStatusCode.NotFound);
    }

    private static FormUrlEncodedContent ReversalForm(Guid transactionId, string token) => new(new Dictionary<string, string>
    {
        ["transactionId"] = transactionId.ToString(),
        ["reason"] = "Customer changed their mind",
        ["__RequestVerificationToken"] = token
    });

    private static FormUrlEncodedContent StockForm(string sku, int delta, string token) => new(new Dictionary<string, string>
    {
        ["productSku"] = sku,
        ["quantityDelta"] = delta.ToString(),
        ["reasonCode"] = "CountCorrection",
        ["__RequestVerificationToken"] = token
    });

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains($"\"status\":{(int)expected}", body);
        Assert.Contains("\"traceId\"", body);
    }

    [Fact]
    public async Task ResponsesWithOnlyAStatus_BecomeProblemDetails()
    {
        // Arrange
        var client = CreateClient(_factory);
        await LoginAsync(client, "ben@apexpost.com", DemoStaff.PasswordFor("ben@apexpost.com"));
        var token = await GetTokenAsync(client, "/Counter");
        var form = new Dictionary<string, string> { ["TransactionId"] = Guid.NewGuid().ToString(), ["Reason"] = "Check" };

        // Act and assert
        await AssertProblemAsync(await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(form)), HttpStatusCode.BadRequest);
        await AssertProblemAsync(await client.GetAsync("/No/Such/Page"), HttpStatusCode.NotFound);

        // Arrange (next step)
        form["__RequestVerificationToken"] = token;

        // Act and assert
        await AssertProblemAsync(await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(form)), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PartnerApiErrors_AreProblemDetails()
    {
        // Arrange
        var client = CreateClient(_factory, AllowedPartnerIp);

        // Act and assert
        await AssertProblemAsync(await client.GetAsync("/integration/parcel-status"), HttpStatusCode.Unauthorized);

        // Arrange (next step)
        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");

        // Act and assert
        await AssertProblemAsync(await client.GetAsync("/integration/parcel-status"), HttpStatusCode.Unauthorized);

        // Arrange (next step)
        var blocked = CreateClient(_factory, "203.0.113.1");

        // Act and assert
        await AssertProblemAsync(await blocked.GetAsync("/integration/parcel-status"), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("?trackingId=")]
    [InlineData("?trackingId=%20")]
    public async Task PartnerApi_RejectsABlankTrackingId(string query)
    {
        // Arrange
        var client = CreateClient(_factory, AllowedPartnerIp);
        client.DefaultRequestHeaders.Add("X-Api-Key", CounterTestSettings.IntegrationKey);

        // Act and assert
        await AssertProblemAsync(await client.GetAsync("/integration/parcel-status" + query), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MaintenanceMode_ReturnsProblemDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/Catalog";
        context.Response.Body = new MemoryStream();
        var middleware = new MaintenanceModeMiddleware(
            _ => Task.CompletedTask,
            new TestOptionsMonitor<MaintenanceOptions>(new MaintenanceOptions { Enabled = true, RetryAfterSeconds = 120 }));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    private static HttpClient CreateClient(WebApplicationFactory<web::Program> factory, string? clientIp = null, [CallerMemberName] string test = "")
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", clientIp ?? TestClientAddress.For(test));
        return client;
    }

    private static async Task LoginAsync(HttpClient client, string email, string password)
    {
        var token = await GetTokenAsync(client, "/Account/Login");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string path) =>
        Regex.Match(await (await client.GetAsync(path)).Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    private sealed class ConflictOnSaveFactory : ApexPostWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddScoped<IUnitOfWork, AlwaysConflictingUnitOfWork>());
        }

        private sealed class AlwaysConflictingUnitOfWork(InMemoryPersistence persistence) : IUnitOfWork
        {
            public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
                throw new ConcurrencyConflictException("This record was changed by someone else at the same time.");

            public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default) => work(ct);

            public void DiscardPendingChanges() => persistence.DiscardPendingChanges();
        }
    }
}
