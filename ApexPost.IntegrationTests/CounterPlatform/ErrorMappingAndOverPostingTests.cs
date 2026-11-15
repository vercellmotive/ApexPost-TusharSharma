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
    public async Task InvokeAsync_KnownException_WritesProblemDetailsWithItsStatus(Exception failure, int expectedStatus)
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
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains($"\"status\":{expectedStatus}", body);
        Assert.Contains("traceId", body);
        Assert.DoesNotContain("Secret internal detail", body);
    }

    [Fact]
    public async Task PostCounterCreate_ConcurrencyConflict_Returns409ProblemDetails()
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
        var audit = await factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        Assert.Contains(audit, entry => entry.Action == "Create" && entry.StatusCode == StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task PostCounterCreate_OverPostedFields_AreIgnored()
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
        var created = _factory.Database.AllTransactions().Last();
        Assert.NotEqual(postedId, created.Id);
        Assert.Equal(TransactionStatus.Paid, created.Status);
        Assert.Equal("STF003", created.ClerkStaffId);
        Assert.Equal("B001", created.BranchCode.Value);
    }

    [Fact]
    public async Task PostStaffEdit_OverPostedFields_AreIgnored()
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
        var after = _factory.Database.StaffById("STF016");
        Assert.True(after.IsActive);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
        Assert.DoesNotContain(_factory.Database.AllStaff(), person => person.Id == "STF999");
    }

    [Fact]
    public async Task PostRevisePrices_OneCategory_ChangesOnlyThatCategory()
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
    public async Task Reports_AfterAMoneyOrder_TotalsMatchThePersistedSale()
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
        var after = (await reports.GetDailyBranchSalesAsync("B001", today)).Single().ServicesTotal;
        Assert.Equal(before + 10m, after);
    }

    [Fact]
    public async Task VoidRefundAndStockAdjust_InvalidRequest_ReturnProblemDetailsAndAreAudited()
    {
        // Arrange
        var client = CreateClient(_factory);
        await LoginAsync(client, "alice@apexpost.com", DemoStaff.PasswordFor("alice@apexpost.com"));
        var token = await GetTokenAsync(client, "/Counter");
        var unknownId = Guid.NewGuid();
        var known = _factory.Database.AllTransactions().Select(transaction => transaction.Id).ToHashSet();
        var sale = await client.PostAsync("/Counter/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CounterId"] = "C01",
            ["RetailItems[0].ProductSku"] = "SKU-001",
            ["RetailItems[0].Quantity"] = "1",
            ["PaymentMethod"] = "Cash",
            ["__RequestVerificationToken"] = token
        }));
        var saleId = _factory.Database.AllTransactions().Single(transaction => !known.Contains(transaction.Id)).Id;

        // Act
        var voidUnknown = await client.PostAsync("/Counter/Void", ReversalForm(unknownId, token));
        var refundUnknown = await client.PostAsync("/Counter/Refund", ReversalForm(unknownId, token));
        var overdraw = await client.PostAsync("/Stock/Adjust", StockForm("SKU-001", -1000000, token));
        var unknownSku = await client.PostAsync("/Stock/Adjust", StockForm("SKU-404", 1, token));
        var firstVoid = await client.PostAsync("/Counter/Void", ReversalForm(saleId, token));
        var secondVoid = await client.PostAsync("/Counter/Void", ReversalForm(saleId, token));
        var refundAfterVoid = await client.PostAsync("/Counter/Refund", ReversalForm(saleId, token));

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        await AssertProblemAsync(voidUnknown, HttpStatusCode.NotFound);
        await AssertProblemAsync(refundUnknown, HttpStatusCode.NotFound);
        await AssertProblemAsync(overdraw, HttpStatusCode.BadRequest);
        await AssertProblemAsync(unknownSku, HttpStatusCode.NotFound);
        Assert.Equal(HttpStatusCode.Redirect, firstVoid.StatusCode);
        await AssertProblemAsync(secondVoid, HttpStatusCode.BadRequest);
        await AssertProblemAsync(refundAfterVoid, HttpStatusCode.BadRequest);
        var audit = await _factory.Services.GetRequiredService<IAuditStore>().GetAllAsync();
        Assert.Contains(audit, entry => entry.Action == "Void" && entry.StatusCode == StatusCodes.Status404NotFound);
        Assert.Contains(audit, entry => entry.Action == "Void" && entry.StatusCode == StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task AdminActions_UnknownRecord_Return404ProblemDetails()
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

        // Act
        HttpResponseMessage[] responses =
        [
            await client.PostAsync("/Staff/Deactivate/STF999", new FormUrlEncodedContent(form)),
            await client.PostAsync("/Branches/SetStatus/B999", new FormUrlEncodedContent(form)),
            await client.GetAsync("/Staff/Edit/STF999"),
            await client.GetAsync("/Branches/Edit/B999"),
            await client.GetAsync("/Products/Edit/SKU-404"),
        ];

        // Assert
        foreach (var response in responses)
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound);
        }
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
    public async Task AnyEndpoint_StatusWithoutABody_BecomesProblemDetails()
    {
        // Arrange
        var client = CreateClient(_factory);
        await LoginAsync(client, "ben@apexpost.com", DemoStaff.PasswordFor("ben@apexpost.com"));
        var token = await GetTokenAsync(client, "/Counter");
        var withoutToken = new Dictionary<string, string> { ["TransactionId"] = Guid.NewGuid().ToString(), ["Reason"] = "Check" };
        var withToken = new Dictionary<string, string>(withoutToken) { ["__RequestVerificationToken"] = token };

        // Act
        var missingToken = await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(withoutToken));
        var unknownPage = await client.GetAsync("/No/Such/Page");
        // Ben is a counter clerk, so a valid void request is refused by policy.
        var clerkVoid = await client.PostAsync("/Counter/Void", new FormUrlEncodedContent(withToken));

        // Assert
        await AssertProblemAsync(missingToken, HttpStatusCode.BadRequest);
        await AssertProblemAsync(unknownPage, HttpStatusCode.NotFound);
        await AssertProblemAsync(clerkVoid, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetParcelStatus_MissingOrWrongKey_ReturnsProblemDetails()
    {
        // Arrange
        var noKey = CreateClient(_factory, AllowedPartnerIp);
        var wrongKey = CreateClient(_factory, AllowedPartnerIp);
        wrongKey.DefaultRequestHeaders.Add("X-Api-Key", "wrong-key");
        var blocked = CreateClient(_factory, "203.0.113.1");

        // Act
        var missing = await noKey.GetAsync("/integration/parcel-status");
        var wrong = await wrongKey.GetAsync("/integration/parcel-status");
        var fromBlockedAddress = await blocked.GetAsync("/integration/parcel-status");

        // Assert
        await AssertProblemAsync(missing, HttpStatusCode.Unauthorized);
        await AssertProblemAsync(wrong, HttpStatusCode.Unauthorized);
        await AssertProblemAsync(fromBlockedAddress, HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("?trackingId=")]
    [InlineData("?trackingId=%20")]
    public async Task GetParcelStatus_BlankTrackingId_Returns400ProblemDetails(string query)
    {
        // Arrange
        var client = CreateClient(_factory, AllowedPartnerIp);
        client.DefaultRequestHeaders.Add("X-Api-Key", CounterTestSettings.IntegrationKey);

        // Act
        var response = await client.GetAsync("/integration/parcel-status" + query);

        // Assert
        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvokeAsync_MaintenanceModeOn_ReturnsProblemDetails()
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
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Signing in as {email} returned {response.StatusCode}, not a redirect.");
        }
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
