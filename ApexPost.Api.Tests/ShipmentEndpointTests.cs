using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApexPost.Tracking.Domain;

namespace ApexPost.Api.Tests;

public sealed class ShipmentEndpointTests : IDisposable
{
    private const string Shipments = "/api/v1/shipments";
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ShipmentEndpointTests() => _client = _factory.CreateClient();

    public void Dispose() => _factory.Dispose();

    public static object NewShipment(string trackingNumber = "APX-20000001", string branch = "B001") => new
    {
        trackingNumber,
        originBranchCode = branch,
        recipientName = "Mia Fictional",
        recipientPhone = "+61 2 5550 1234",
        recipientAddress = "12 Example Lane",
        city = "Sydney",
        weightKg = 2.5,
    };

    [Fact]
    public async Task Post_returns_201_with_a_location_header_for_the_new_shipment()
    {
        var response = await _client.PostAsJsonAsync(Shipments, NewShipment());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/v1/shipments/APX-20000001", response.Headers.Location?.AbsolutePath);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("APX-20000001", body.GetProperty("trackingNumber").GetString());
        Assert.Equal(0, body.GetProperty("status").GetInt32());

        var fetched = await _client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task Post_with_bad_fields_returns_400_validation_problem_details_naming_each_field()
    {
        var response = await _client.PostAsJsonAsync(Shipments, new
        {
            trackingNumber = "APX-123",
            originBranchCode = "B001",
            recipientPhone = "call me",
            recipientAddress = "12 Example Lane",
            city = "Sydney",
            weightKg = 70.5,
        });

        var errors = await ReadValidationErrors(response);
        Assert.Equal(["recipientName", "recipientPhone", "trackingNumber", "weightKg"], errors.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("+61 2 5550 1234", true)]
    [InlineData("0255501234", true)]
    [InlineData("(02) 5550-1234", true)]
    [InlineData("+1 555 0100", true)]
    [InlineData("5550123", false)]
    [InlineData("+61 2 5550 1234 5678", true)]
    [InlineData("+61 2 5550 1234 56789", false)]
    [InlineData("02--5550", false)]
    [InlineData("phone 0255501234", false)]
    public async Task Phone_numbers_need_8_to_15_digits_in_a_plain_layout(string phone, bool valid)
    {
        var request = JsonSerializer.SerializeToNode(NewShipment(), JsonSerializerOptions.Web)!;
        request["recipientPhone"] = phone;

        var response = await _client.PostAsJsonAsync(Shipments, request);

        Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_an_unknown_week_3_branch_returns_400_on_that_field()
    {
        var response = await _client.PostAsJsonAsync(Shipments, NewShipment(branch: "B999"));

        var errors = await ReadValidationErrors(response);
        Assert.Equal(["originBranchCode"], errors.Keys);
    }

    [Fact]
    public async Task Post_with_a_duplicate_tracking_number_returns_409()
    {
        _factory.Store.Seed(Make.Shipment("APX-20000001"));

        var response = await _client.PostAsJsonAsync(Shipments, NewShipment());

        var problem = await ReadProblem(response, HttpStatusCode.Conflict);
        Assert.Equal("Duplicate tracking number", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("application/xml")]
    public async Task Post_with_the_wrong_content_type_returns_415_problem_details(string mediaType)
    {
        var content = new StringContent(JsonSerializer.Serialize(NewShipment()), Encoding.UTF8, mediaType);

        var response = await _client.PostAsync(Shipments, content);

        await ReadProblem(response, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task Malformed_json_returns_400_problem_details()
    {
        var response = await _client.PostAsync(Shipments, new StringContent("{\"trackingNumber\":", Encoding.UTF8, "application/json"));

        await ReadProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_returns_200_for_a_known_shipment_and_404_problem_details_otherwise()
    {
        _factory.Store.Seed(Make.Shipment("APX-10000001"));

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync($"{Shipments}/APX-10000001")).StatusCode);
        var problem = await ReadProblem(await _client.GetAsync($"{Shipments}/APX-99999999"), HttpStatusCode.NotFound);
        Assert.Contains("APX-99999999", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_badly_formed_tracking_number_does_not_match_the_route_constraint()
    {
        await ReadProblem(await _client.GetAsync($"{Shipments}/12345"), HttpStatusCode.NotFound);
        await ReadProblem(await _client.GetAsync($"{Shipments}/APX-1234567٣"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_pages_with_a_default_of_20_and_filters_by_status_and_city()
    {
        for (var index = 0; index < 25; index++)
        {
            var shipment = Make.Shipment($"APX-3{index:D7}", TestClock.Start.AddMinutes(-index), city: index % 2 == 0 ? "Sydney" : "Perth");
            if (index < 3)
            {
                shipment.MoveTo(ShipmentStatus.PickedUp, TestClock.Start);
            }

            _factory.Store.Seed(shipment);
        }

        var firstPage = await _client.GetFromJsonAsync<JsonElement>(Shipments);
        Assert.Equal(20, firstPage.GetProperty("items").GetArrayLength());
        Assert.Equal(25, firstPage.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, firstPage.GetProperty("totalPages").GetInt32());
        Assert.Equal("APX-30000000", firstPage.GetProperty("items")[0].GetProperty("trackingNumber").GetString());

        var secondPage = await _client.GetFromJsonAsync<JsonElement>($"{Shipments}?page=2");
        Assert.Equal(5, secondPage.GetProperty("items").GetArrayLength());

        var filtered = await _client.GetFromJsonAsync<JsonElement>($"{Shipments}?status=PickedUp&city=sydney&pageSize=50");
        Assert.Equal(["APX-30000000", "APX-30000002"], filtered.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("trackingNumber").GetString()));
    }

    [Theory]
    [InlineData("pageSize=51", "pageSize")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("status=Lost", "status")]
    [InlineData("status=42", "status")]
    public async Task List_refuses_bad_paging_and_filters_with_400(string query, string field)
    {
        var errors = await ReadValidationErrors(await _client.GetAsync($"{Shipments}?{query}"));

        Assert.Contains(field, errors.Keys);
    }

    [Fact]
    public async Task Put_replaces_the_details_and_refuses_a_closed_shipment()
    {
        _factory.Store.Seed(Make.Shipment("APX-10000001"));
        _factory.Store.Seed(Make.ShipmentIn(ShipmentStatus.Delivered, "APX-10000002"));
        var details = new { recipientName = "Noah Sample", recipientPhone = "0255509876", recipientAddress = "3 Fictional Way", city = "Perth", weightKg = 4 };

        var updated = await _client.PutAsJsonAsync($"{Shipments}/APX-10000001", details);
        var closed = await _client.PutAsJsonAsync($"{Shipments}/APX-10000002", details);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Perth", (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("city").GetString());
        await ReadProblem(closed, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Patch_status_accepts_a_name_or_a_number_and_returns_409_for_an_invalid_move()
    {
        _factory.Store.Seed(Make.Shipment("APX-10000001"));
        var url = $"{Shipments}/APX-10000001/status";

        var byName = await _client.PatchAsJsonAsync(url, new { status = "PickedUp" });
        var byNumber = await _client.PatchAsJsonAsync(url, new { status = (int)ShipmentStatus.InTransit });
        var skipped = await _client.PatchAsJsonAsync(url, new { status = "Delivered" });

        Assert.Equal(HttpStatusCode.OK, byName.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byNumber.StatusCode);
        var problem = await ReadProblem(skipped, HttpStatusCode.Conflict);
        Assert.Equal("Invalid status transition", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":\"Lost\"}")]
    [InlineData("{\"status\":42}")]
    public async Task Patch_status_without_a_valid_status_returns_400(string body)
    {
        _factory.Store.Seed(Make.Shipment("APX-10000001"));

        var response = await _client.PatchAsync($"{Shipments}/APX-10000001/status", new StringContent(body, Encoding.UTF8, "application/json"));

        await ReadProblem(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_returns_204_while_created_and_409_after_pickup()
    {
        _factory.Store.Seed(Make.Shipment("APX-10000001"));
        _factory.Store.Seed(Make.ShipmentIn(ShipmentStatus.PickedUp, "APX-10000002"));

        var deleted = await _client.DeleteAsync($"{Shipments}/APX-10000001");
        var refused = await _client.DeleteAsync($"{Shipments}/APX-10000002");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, deleted.Content.Headers.ContentLength ?? 0);
        await ReadProblem(refused, HttpStatusCode.Conflict);
        await ReadProblem(await _client.DeleteAsync($"{Shipments}/APX-10000001"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_unhandled_error_returns_500_problem_details_without_the_exception_outside_development()
    {
        _factory.StoreFailure = new InvalidOperationException("Sensitive internal detail");

        var response = await _client.GetAsync($"{Shipments}/APX-10000001");

        var problem = await ReadProblem(response, HttpStatusCode.InternalServerError);
        var text = problem.GetRawText();
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.False(problem.TryGetProperty("exception", out _));
        Assert.DoesNotContain("Sensitive internal detail", text);
        Assert.DoesNotContain(" at ", text);
    }

    [Fact]
    public async Task In_development_the_500_response_shows_the_exception()
    {
        using var factory = new ApiFactory("Development") { StoreFailure = new InvalidOperationException("Visible to the developer") };

        var response = await factory.CreateClient().GetAsync($"{Shipments}/APX-10000001");

        var problem = await ReadProblem(response, HttpStatusCode.InternalServerError);
        Assert.Equal("Visible to the developer", problem.GetProperty("detail").GetString());
        Assert.Contains("InvalidOperationException", problem.GetProperty("exception").GetString());
    }

    [Fact]
    public async Task Wrong_verbs_and_unknown_routes_also_get_problem_details()
    {
        await ReadProblem(await _client.GetAsync("/api/nothing-here"), HttpStatusCode.NotFound);
        await ReadProblem(await _client.PutAsJsonAsync(Shipments, NewShipment()), HttpStatusCode.MethodNotAllowed);
    }

    internal static async Task<JsonElement> ReadProblem(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("title").GetString()));
        return problem;
    }

    internal static async Task<Dictionary<string, string[]>> ReadValidationErrors(HttpResponseMessage response)
    {
        var problem = await ReadProblem(response, HttpStatusCode.BadRequest);
        return problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
    }
}
