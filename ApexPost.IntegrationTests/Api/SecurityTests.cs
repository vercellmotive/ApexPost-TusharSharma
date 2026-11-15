using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.Security;
using ApexPost.Tracking.Domain;
using Microsoft.AspNetCore.Hosting;
using static ApexPost.IntegrationTests.Api.ShipmentEndpointTests;
using ApexPost.Testing.Tracking;
using ApexPost.Testing.Api;

namespace ApexPost.IntegrationTests.Api;

public sealed class SecurityTests : IDisposable
{
    private const string Shipment = "/api/v2/shipments/APX-10000001";
    private readonly ApiFactory _factory = new();

    public SecurityTests() => _factory.Store.Seed(Make.Shipment("APX-10000001"));

    public void Dispose() => _factory.Dispose();

    private static object ScanBatch(string courierId) => new
    {
        scans = new[] { new { scanId = Make.NewScanId(), scanType = "Pickup", courierId, location = "Depot", scannedAtUtc = TestTime.Start.AddMinutes(30) } },
    };

    [Fact]
    public async Task GetShipment_NoToken_Returns401WithBearerChallenge()
    {
        // Act
        var response = await _factory.CreateAnonymousClient().GetAsync(Shipment);

        // Assert
        var problem = await ReadProblem(response, HttpStatusCode.Unauthorized);

        Assert.Contains("needs a bearer access token", problem.GetProperty("detail").GetString());
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task GetShipment_ExpiredToken_Returns401SayingItExpired()
    {
        // Arrange
        var expired = ApiFactory.Token([ApexPostRoles.Courier], expires: ApiFactory.ExpiredAt);
        var client = _factory.CreateClient(expired);

        // Act
        var response = await client.GetAsync(Shipment);

        // Assert
        var problem = await ReadProblem(response, HttpStatusCode.Unauthorized);
        Assert.Contains("expired", problem.GetProperty("detail").GetString());
    }

    public static TheoryData<string> InvalidTokens => new()
    {
        ApiFactory.Token([ApexPostRoles.Courier], issuer: "https://someone-else.example"),
        ApiFactory.Token([ApexPostRoles.Courier], audience: "another-api"),
        ApiFactory.Token([ApexPostRoles.Courier], signingKey: Convert.ToBase64String(new byte[32])),
        "not-a-jwt",
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task GetShipment_WrongIssuerAudienceOrKey_Returns401(string token)
    {
        // Arrange
        var client = _factory.CreateClient(token);

        // Act
        var response = await client.GetAsync(Shipment);

        // Assert
        var problem = await ReadProblem(response, HttpStatusCode.Unauthorized);
        Assert.Equal("The access token is not valid.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetShipment_AnySignedInRole_Returns200()
    {
        // Arrange
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));
        var admin = _factory.CreateClient(ApiFactory.Token([ApexPostRoles.Admin], courierId: null));

        // Act
        var courierRead = await courier.GetAsync(Shipment);
        var adminList = await admin.GetAsync("/api/v2/shipments");
        var adminScans = await admin.GetAsync($"{Shipment}/scans");

        // Assert
        Assert.Equal(HttpStatusCode.OK, courierRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminScans.StatusCode);
    }

    [Fact]
    public async Task DeleteShipment_ByRole_OnlyAdminSucceedsAndCourierGets403()
    {
        // Arrange
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));
        var admin = _factory.CreateClient(ApiFactory.Token([ApexPostRoles.Admin], courierId: null));

        // Act
        var courierDelete = await courier.DeleteAsync(Shipment);
        var adminDelete = await admin.DeleteAsync(Shipment);

        // Assert
        var problem = await ReadProblem(courierDelete, HttpStatusCode.Forbidden);
        Assert.Equal("Your role does not allow this action.", problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NoContent, adminDelete.StatusCode);
    }

    [Fact]
    public async Task StatusAndScanWrites_ByRole_OnlyCourierSucceeds()
    {
        // Arrange
        var admin = _factory.CreateClient(ApiFactory.Token([ApexPostRoles.Admin], courierId: null));
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));

        // Act
        var adminStatus = await admin.PatchAsJsonAsync($"{Shipment}/status", new { status = "PickedUp" });
        var adminScans = await admin.PostAsJsonAsync($"{Shipment}/scans", ScanBatch(Make.CourierId));
        var courierStatus = await courier.PatchAsJsonAsync($"{Shipment}/status", new { status = "PickedUp" });
        var courierScans = await courier.PostAsJsonAsync($"{Shipment}/scans", ScanBatch(Make.CourierId));

        // Assert
        await ReadProblem(adminStatus, HttpStatusCode.Forbidden);
        await ReadProblem(adminScans, HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, courierStatus.StatusCode);
        Assert.Equal(HttpStatusCode.OK, courierScans.StatusCode);
    }

    [Fact]
    public async Task CreateOrEditShipment_CourierRole_Returns403()
    {
        // Arrange
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));

        // Act
        var create = await courier.PostAsJsonAsync("/api/v2/shipments", NewShipment());
        var edit = await courier.PutAsJsonAsync(Shipment, NewShipment());

        // Assert
        await ReadProblem(create, HttpStatusCode.Forbidden);
        await ReadProblem(edit, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostScans_AnotherCouriersId_Returns403AndStoresNothing()
    {
        // Arrange
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));

        // Act
        var response = await courier.PostAsJsonAsync($"{Shipment}/scans", ScanBatch("CR-2002"));

        // Assert
        var problem = await ReadProblem(response, HttpStatusCode.Forbidden);
        Assert.Contains("CR-2002", problem.GetProperty("detail").GetString());
        Assert.Empty(_factory.Store.AllScans);
    }

    [Fact]
    public async Task PostScans_TokenWithoutCourierId_Returns403()
    {
        // Arrange
        var noClaim = _factory.CreateClient(ApiFactory.Token([ApexPostRoles.Courier], courierId: null));

        // Act
        var response = await noClaim.PostAsJsonAsync($"{Shipment}/scans", ScanBatch(Make.CourierId));

        // Assert
        await ReadProblem(response, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PartnerGet_ApiKey_RequiredAndRecipientHidden()
    {
        // Arrange
        const string route = "/api/v2/partner/shipments/APX-10000001";
        var anonymous = _factory.CreateAnonymousClient();
        var bearer = _factory.CreateClient();
        var wrongKey = new HttpRequestMessage(HttpMethod.Get, route) { Headers = { { "X-Api-Key", "wrong-key-0123456789abcdef0123456789" } } };
        var rightKey = new HttpRequestMessage(HttpMethod.Get, route) { Headers = { { "X-Api-Key", ApiFactory.PartnerKey } } };

        // Act
        var missing = await anonymous.GetAsync(route);
        var wrong = await anonymous.SendAsync(wrongKey);
        var bearerOnly = await bearer.GetAsync(route);
        var ok = await anonymous.SendAsync(rightKey);

        // Assert
        await ReadProblem(missing, HttpStatusCode.Unauthorized);
        await ReadProblem(wrong, HttpStatusCode.Unauthorized);
        await ReadProblem(bearerOnly, HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Created", body.GetProperty("status").GetString());
        Assert.Equal("Sydney", body.GetProperty("destinationCity").GetString());
        Assert.DoesNotContain("5550", body.GetRawText());
        Assert.DoesNotContain("Mia", body.GetRawText());
    }

    [Fact]
    public async Task PartnerGet_UnknownShipmentOrV1Route_Returns404()
    {
        // Arrange
        var unknown = new HttpRequestMessage(HttpMethod.Get, "/api/v2/partner/shipments/APX-99999999") { Headers = { { "X-Api-Key", ApiFactory.PartnerKey } } };
        var v1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/partner/shipments/APX-10000001") { Headers = { { "X-Api-Key", ApiFactory.PartnerKey } } };
        var client = _factory.CreateAnonymousClient();

        // Act
        var unknownResponse = await client.SendAsync(unknown);
        var v1Response = await client.SendAsync(v1);

        // Assert
        await ReadProblem(unknownResponse, HttpStatusCode.NotFound);
        Assert.NotEqual(HttpStatusCode.OK, v1Response.StatusCode);
    }

    [Fact]
    public async Task PartnerKeys_ShorterThanMinimum_AreIgnored()
    {
        // Arrange
        using var factory = new ApiFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseSetting("PartnerApiKeys:Keys:Weak", "short")).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/partner/shipments/APX-10000001") { Headers = { { "X-Api-Key", "short" } } };

        // Act
        var response = await client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DevToken_DevelopmentAndProduction_IssuesWorkingTokenOnlyInDevelopment()
    {
        // Arrange
        using var development = new ApiFactory("Development");
        development.Store.Seed(Make.Shipment("APX-10000001"));
        using var production = new ApiFactory("Production");
        var anonymous = development.CreateAnonymousClient();

        // Act
        var issued = await anonymous.PostAsJsonAsync("/dev/token", new { roles = new[] { "Courier" }, courierId = "CR-1001" });
        var token = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        var readWithIssuedToken = await development.CreateClient(token).GetAsync(Shipment);
        var refused = await anonymous.PostAsJsonAsync("/dev/token", new { roles = new[] { "Courier" } });
        var productionSignedIn = await production.CreateClient().PostAsJsonAsync("/dev/token", new { roles = new[] { "Admin" } });
        var productionAnonymous = await production.CreateAnonymousClient().PostAsJsonAsync("/dev/token", new { roles = new[] { "Admin" } });

        // Assert
        Assert.Equal(HttpStatusCode.OK, readWithIssuedToken.StatusCode);
        await ReadProblem(refused, HttpStatusCode.BadRequest);
        // In Production the route does not exist: 404 to a signed-in caller, and 401 to an anonymous one, who learns nothing.
        Assert.Equal(HttpStatusCode.NotFound, productionSignedIn.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, productionAnonymous.StatusCode);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task Hsts_HttpsRequestPerEnvironment_SentOnlyOutsideDevelopment(string environment, bool expected)
    {
        // Arrange
        using var factory = new ApiFactory(environment);
        var client = factory.CreateClient();
        // HSTS is never sent to localhost, so the request names a real-looking host.
        client.BaseAddress = new Uri("https://api.apexpost.example");

        // Act
        var response = await client.GetAsync("/api/v2/shipments");

        // Assert
        Assert.Equal(expected, response.Headers.Contains("Strict-Transport-Security"));
    }

    [Theory]
    [InlineData(29)]
    [InlineData(60)]
    [InlineData(100_000)]
    public async Task PostShipment_LongPhoneEndingInBadCharacter_Returns400(int digits)
    {
        // Arrange
        var request = JsonSerializer.SerializeToNode(NewShipment(), JsonSerializerOptions.Web)!;
        request["recipientPhone"] = new string('1', digits) + "x";
        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/v2/shipments", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void PhoneNumberAttribute_Pattern_UsesNonBacktrackingEngine()
    {
        // Arrange
        // A timing assertion would depend on the machine, so the test checks the cause instead: the engine that cannot
        // backtrack, which matches in time proportional to the input whatever the input is.
        var pattern = ApexPost.Api.Validation.PhoneNumberAttribute.Shape();
        var attribute = new ApexPost.Api.Validation.PhoneNumberAttribute();

        // Act
        var options = pattern.Options;
        var longBadInputValid = attribute.IsValid(new string('1', 5000) + "x");
        var normalInputValid = attribute.IsValid("+61 2 5550 1234");

        // Assert
        Assert.True(options.HasFlag(System.Text.RegularExpressions.RegexOptions.NonBacktracking));
        Assert.False(longBadInputValid);
        Assert.True(normalInputValid);
    }

    [Fact]
    public async Task DevToken_DevelopmentWithSettingOff_IssuesNoToken()
    {
        // Arrange
        using var development = new ApiFactory("Development");
        var off = development.WithWebHostBuilder(builder => builder.UseSetting(ApexPost.Api.Security.DevelopmentTokens.AllowSetting, "false"));

        var withSetting = await development.CreateAnonymousClient().PostAsJsonAsync("/dev/token", new { roles = new[] { "Admin" } });

        // Act
        var withoutSetting = await off.CreateClient().PostAsJsonAsync("/dev/token", new { roles = new[] { "Admin" } });

        // Assert
        Assert.Equal(HttpStatusCode.OK, withSetting.StatusCode);
        await AssertNoTokenIssuedAsync(withoutSetting);
    }

    [Fact]
    public async Task DevToken_ProductionWithSettingOn_IssuesNoToken()
    {
        // Arrange
        using var production = new ApiFactory("Production");
        var on = production.WithWebHostBuilder(builder => builder.UseSetting(ApexPost.Api.Security.DevelopmentTokens.AllowSetting, "true"));

        // Act
        var response = await on.CreateClient().PostAsJsonAsync("/dev/token", new { roles = new[] { "Admin" } });

        // Assert
        await AssertNoTokenIssuedAsync(response);
    }

    // The route does not exist, so an anonymous caller gets the same 401 as for any other path and learns nothing.
    private static async Task AssertNoTokenIssuedAsync(HttpResponseMessage response)
    {
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DevToken_NoCallerAddress_Returns404()
    {
        // Arrange
        using var development = new ApiFactory("Development");
        var client = development.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add(ApiFactory.NoAddressHeader, "1");

        // Act
        var response = await client.PostAsJsonAsync("/dev/token", new { roles = new[] { "Admin" } });
        var noAddressIsLocal = ApexPost.Api.Security.DevelopmentTokens.IsLocalCaller(null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
        Assert.False(noAddressIsLocal);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("203.0.113.9", false)]
    [InlineData("10.0.2.15", false)]
    public void IsLocalCaller_Address_TrueOnlyForLoopback(string address, bool expected)
    {
        // Arrange
        var caller = System.Net.IPAddress.Parse(address);

        // Act
        var local = ApexPost.Api.Security.DevelopmentTokens.IsLocalCaller(caller);

        // Assert
        Assert.Equal(expected, local);
    }

    [Fact]
    public async Task PartnerGet_OverRateLimit_Returns429WithRetryAfter()
    {
        // Arrange
        using var factory = new ApiFactory();
        var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:PartnerPerMinute", "3"));
        var client = limited.CreateClient();
        HttpResponseMessage Request()
        {
            var message = new HttpRequestMessage(HttpMethod.Get, "/api/v2/partner/shipments/APX-10000001");
            message.Headers.Add("X-Api-Key", "wrong-key-0123456789abcdef0123456789");
            return client.SendAsync(message).GetAwaiter().GetResult();
        }
        var first = new[] { Request(), Request(), Request() };

        // Act
        var fourth = Request();

        // Assert
        Assert.All(first, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        await ReadProblem(fourth, HttpStatusCode.TooManyRequests);
        Assert.True(fourth.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task AnyEndpoint_OverGeneralRateLimit_Returns429()
    {
        // Arrange
        using var factory = new ApiFactory();
        var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:GeneralPerMinute", "2"));
        var client = limited.CreateClient();

        await client.GetAsync("/api/v2/shipments");
        await client.GetAsync("/api/v2/shipments");

        // Act
        var third = await client.GetAsync("/api/v2/shipments");

        // Assert
        await ReadProblem(third, HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void Startup_WeakSigningKey_RefusesToStart()
    {
        // Arrange
        using var factory = new ApiFactory();
        var weak = factory.WithWebHostBuilder(builder => builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[16])));

        // Act
        var error = Record.Exception(() => weak.CreateClient());

        // Assert
        Assert.NotNull(error);
        Assert.Contains("Jwt:SigningKey", error.ToString());
    }
}
