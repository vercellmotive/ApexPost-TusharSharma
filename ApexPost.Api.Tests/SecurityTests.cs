using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApexPost.Api.Security;
using ApexPost.Tracking.Domain;
using static ApexPost.Api.Tests.ShipmentEndpointTests;

namespace ApexPost.Api.Tests;

public sealed class SecurityTests : IDisposable
{
    private const string Shipment = "/api/v2/shipments/APX-10000001";
    private readonly ApiFactory _factory = new();

    public SecurityTests() => _factory.Store.Seed(Make.Shipment("APX-10000001"));

    public void Dispose() => _factory.Dispose();

    private static object ScanBatch(string courierId) => new
    {
        scans = new[] { new { scanId = Make.NewScanId(), scanType = "Pickup", courierId, location = "Depot", scannedAtUtc = TestClock.Start.AddMinutes(30) } },
    };

    [Fact]
    public async Task No_token_returns_401_problem_details_with_a_bearer_challenge()
    {
        var response = await _factory.CreateAnonymousClient().GetAsync(Shipment);

        var problem = await ReadProblem(response, HttpStatusCode.Unauthorized);
        Assert.Contains("needs a bearer access token", problem.GetProperty("detail").GetString());
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
    }

    [Fact]
    public async Task An_expired_token_returns_401_saying_so()
    {
        var expired = ApiFactory.Token([ApexPostRoles.Courier], expires: DateTimeOffset.UtcNow.AddMinutes(-5));

        var problem = await ReadProblem(await _factory.CreateClient(expired).GetAsync(Shipment), HttpStatusCode.Unauthorized);

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
    public async Task A_token_with_the_wrong_issuer_audience_or_key_returns_401(string token)
    {
        var problem = await ReadProblem(await _factory.CreateClient(token).GetAsync(Shipment), HttpStatusCode.Unauthorized);

        Assert.Equal("The access token is not valid.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Any_signed_in_user_can_read_shipments()
    {
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));
        var admin = _factory.CreateClient(ApiFactory.Token([ApexPostRoles.Admin], courierId: null));

        Assert.Equal(HttpStatusCode.OK, (await courier.GetAsync(Shipment)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v2/shipments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Shipment}/scans")).StatusCode);
    }

    [Fact]
    public async Task Only_an_admin_can_delete_and_a_courier_gets_403()
    {
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));
        var admin = _factory.CreateClient(ApiFactory.Token([ApexPostRoles.Admin], courierId: null));

        var problem = await ReadProblem(await courier.DeleteAsync(Shipment), HttpStatusCode.Forbidden);
        Assert.Equal("Your role does not allow this action.", problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(Shipment)).StatusCode);
    }

    [Fact]
    public async Task Only_a_courier_can_change_status_or_upload_scans()
    {
        var admin = _factory.CreateClient(ApiFactory.Token([ApexPostRoles.Admin], courierId: null));
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));

        await ReadProblem(await admin.PatchAsJsonAsync($"{Shipment}/status", new { status = "PickedUp" }), HttpStatusCode.Forbidden);
        await ReadProblem(await admin.PostAsJsonAsync($"{Shipment}/scans", ScanBatch(Make.CourierId)), HttpStatusCode.Forbidden);
        Assert.Equal(HttpStatusCode.OK, (await courier.PatchAsJsonAsync($"{Shipment}/status", new { status = "PickedUp" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await courier.PostAsJsonAsync($"{Shipment}/scans", ScanBatch(Make.CourierId))).StatusCode);
    }

    [Fact]
    public async Task A_courier_cannot_create_or_edit_shipments()
    {
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));

        await ReadProblem(await courier.PostAsJsonAsync("/api/v2/shipments", NewShipment()), HttpStatusCode.Forbidden);
        await ReadProblem(await courier.PutAsJsonAsync(Shipment, NewShipment()), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_courier_uploading_scans_under_another_courier_id_gets_403_and_nothing_is_stored()
    {
        var courier = _factory.CreateClient(ApiFactory.Token(ApexPostRoles.Courier));

        var problem = await ReadProblem(await courier.PostAsJsonAsync($"{Shipment}/scans", ScanBatch("CR-2002")), HttpStatusCode.Forbidden);

        Assert.Contains("CR-2002", problem.GetProperty("detail").GetString());
        Assert.Empty(_factory.Store.AllScans);
    }

    [Fact]
    public async Task A_courier_token_without_a_courier_id_cannot_upload()
    {
        var noClaim = _factory.CreateClient(ApiFactory.Token([ApexPostRoles.Courier], courierId: null));

        await ReadProblem(await noClaim.PostAsJsonAsync($"{Shipment}/scans", ScanBatch(Make.CourierId)), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_partner_route_needs_a_valid_api_key_and_hides_the_recipient()
    {
        const string route = "/api/v2/partner/shipments/APX-10000001";
        var anonymous = _factory.CreateAnonymousClient();

        var missing = await anonymous.GetAsync(route);
        var wrongKey = new HttpRequestMessage(HttpMethod.Get, route) { Headers = { { "X-Api-Key", "wrong-key-0123456789abcdef0123456789" } } };
        var rightKey = new HttpRequestMessage(HttpMethod.Get, route) { Headers = { { "X-Api-Key", ApiFactory.PartnerKey } } };
        var bearerOnly = await _factory.CreateClient().GetAsync(route);

        await ReadProblem(missing, HttpStatusCode.Unauthorized);
        await ReadProblem(await anonymous.SendAsync(wrongKey), HttpStatusCode.Unauthorized);
        await ReadProblem(bearerOnly, HttpStatusCode.Unauthorized);
        var ok = await anonymous.SendAsync(rightKey);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Created", body.GetProperty("status").GetString());
        Assert.Equal("Sydney", body.GetProperty("destinationCity").GetString());
        Assert.DoesNotContain("5550", body.GetRawText());
        Assert.DoesNotContain("Mia", body.GetRawText());
    }

    [Fact]
    public async Task The_partner_route_returns_404_for_an_unknown_shipment_and_only_exists_in_v2()
    {
        var unknown = new HttpRequestMessage(HttpMethod.Get, "/api/v2/partner/shipments/APX-99999999") { Headers = { { "X-Api-Key", ApiFactory.PartnerKey } } };
        var v1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/partner/shipments/APX-10000001") { Headers = { { "X-Api-Key", ApiFactory.PartnerKey } } };
        var client = _factory.CreateAnonymousClient();

        await ReadProblem(await client.SendAsync(unknown), HttpStatusCode.NotFound);
        Assert.NotEqual(HttpStatusCode.OK, (await client.SendAsync(v1)).StatusCode);
    }

    [Fact]
    public async Task Short_configured_partner_keys_are_ignored()
    {
        using var factory = new ApiFactory();
        var client = factory.WithWebHostBuilder(builder => builder.UseSetting("PartnerApiKeys:Keys:Weak", "short")).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/partner/shipments/APX-10000001") { Headers = { { "X-Api-Key", "short" } } };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Development_issues_working_tokens_and_production_has_no_token_endpoint()
    {
        using var development = new ApiFactory("Development");
        development.Store.Seed(Make.Shipment("APX-10000001"));
        using var production = new ApiFactory("Production");
        var anonymous = development.CreateAnonymousClient();

        var issued = await anonymous.PostAsJsonAsync("/dev/token", new { roles = new[] { "Courier" }, courierId = "CR-1001" });
        var token = (await issued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
        var refused = await anonymous.PostAsJsonAsync("/dev/token", new { roles = new[] { "Courier" } });

        Assert.Equal(HttpStatusCode.OK, (await development.CreateClient(token).GetAsync(Shipment)).StatusCode);
        await ReadProblem(refused, HttpStatusCode.BadRequest);

        // In Production the route does not exist: 404 to a signed-in caller, and 401 to an anonymous one, who learns nothing.
        Assert.Equal(HttpStatusCode.NotFound, (await production.CreateClient().PostAsJsonAsync("/dev/token", new { roles = new[] { "Admin" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await production.CreateAnonymousClient().PostAsJsonAsync("/dev/token", new { roles = new[] { "Admin" } })).StatusCode);
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Development", false)]
    public async Task Hsts_is_sent_over_https_outside_development(string environment, bool expected)
    {
        using var factory = new ApiFactory(environment);
        var client = factory.CreateClient();
        // HSTS is never sent to localhost, so the request names a real-looking host.
        client.BaseAddress = new Uri("https://api.apexpost.example");

        var response = await client.GetAsync("/api/v2/shipments");

        Assert.Equal(expected, response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task The_app_will_not_start_without_a_strong_signing_key()
    {
        using var factory = new ApiFactory();
        var weak = factory.WithWebHostBuilder(builder => builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(new byte[16])));

        var error = Assert.ThrowsAny<Exception>(() => weak.CreateClient());

        Assert.Contains("Jwt:SigningKey", error.ToString());
        await Task.CompletedTask;
    }
}
