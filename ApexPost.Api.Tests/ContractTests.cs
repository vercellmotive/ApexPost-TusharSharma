using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApexPost.Api.Tests;

/// <summary>
/// The OpenAPI documents the API serves must match the contracts committed in /contracts. A change to the API that
/// changes a contract fails here until the contract file is updated on purpose, and VERSIONING.md says whether the
/// change is breaking. To update the files, run the tests with APEXPOST_UPDATE_CONTRACTS=1.
/// </summary>
public sealed class ContractTests : IDisposable
{
    private readonly ApiFactory _development = new("Development");

    public void Dispose() => _development.Dispose();

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public async Task The_served_document_matches_the_committed_contract(string version)
    {
        var served = await _development.CreateAnonymousClient().GetStringAsync($"/openapi/{version}.json");
        var path = Path.Combine(RepositoryRoot(), "contracts", $"{version}.json");

        if (Environment.GetEnvironmentVariable("APEXPOST_UPDATE_CONTRACTS") == "1")
        {
            await File.WriteAllTextAsync(path, Pretty(served) + "\n");
        }

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(served), JsonNode.Parse(await File.ReadAllTextAsync(path))),
            $"/openapi/{version}.json differs from contracts/{version}.json. If the change is intended, update the contract and VERSIONING.md.");
    }

    [Fact]
    public async Task Both_documents_are_titled_versioned_and_v1_is_deprecated()
    {
        var client = _development.CreateClient();
        var v1 = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!;
        var v2 = JsonNode.Parse(await client.GetStringAsync("/openapi/v2.json"))!;

        Assert.Equal("ApexPost Logistics API", (string?)v1["info"]!["title"]);
        Assert.Equal("v2", (string?)v2["info"]!["version"]);
        Assert.EndsWith(".example", (string?)v2["info"]!["contact"]!["email"]);
        Assert.All(Operations(v1), operation => Assert.True((bool?)operation["deprecated"]));
        Assert.All(Operations(v2), operation => Assert.Null(operation["deprecated"]));
        Assert.All(Operations(v2), operation => Assert.False(string.IsNullOrEmpty((string?)operation["operationId"])));
        Assert.Equal(
            ["CreateShipment", "DeleteShipment", "GetPartnerShipment", "GetShipment", "ListScans", "ListShipments", "UpdateShipment", "UpdateShipmentStatus", "UploadScans"],
            Operations(v2).Select(operation => (string)operation["operationId"]!).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_operation_declares_its_error_responses_and_a_tag()
    {
        var v2 = JsonNode.Parse(await _development.CreateClient().GetStringAsync("/openapi/v2.json"))!;

        foreach (var operation in Operations(v2))
        {
            var codes = operation["responses"]!.AsObject().Select(response => response.Key).ToList();
            Assert.Contains("500", codes);
            Assert.Contains("401", codes);
            Assert.Contains(codes, code => code is "200" or "201" or "204");
            Assert.Contains((string)operation["tags"]![0]!, new[] { "Shipments", "Scans", "Partner" });
            Assert.False(string.IsNullOrEmpty((string?)operation["summary"]));
        }
    }

    [Fact]
    public async Task V2_declares_the_bearer_and_api_key_schemes_and_uses_them()
    {
        var v2 = JsonNode.Parse(await _development.CreateClient().GetStringAsync("/openapi/v2.json"))!;

        var schemes = v2["components"]!["securitySchemes"]!;
        Assert.Equal("bearer", (string?)schemes["Bearer"]!["scheme"]);
        Assert.Equal("X-Api-Key", (string?)schemes["ApiKey"]!["name"]);
        var partner = v2["paths"]!["/api/v2/partner/shipments/{trackingNumber}"]!["get"]!;
        Assert.NotNull(partner["security"]![0]!["ApiKey"]);
        Assert.NotNull(v2["paths"]!["/api/v2/shipments"]!["get"]!["security"]![0]!["Bearer"]);
    }

    [Theory]
    [InlineData("/openapi/v1.json")]
    [InlineData("/openapi/v2.json")]
    [InlineData("/swagger/index.html")]
    public async Task The_documents_and_swagger_ui_exist_only_in_development(string path)
    {
        using var production = new ApiFactory("Production");

        Assert.Equal(HttpStatusCode.OK, (await _development.CreateClient().GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await production.CreateClient().GetAsync(path)).StatusCode);
    }

    private static IEnumerable<JsonNode> Operations(JsonNode document) =>
        document["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject().Select(operation => operation.Value!));

    private static string Pretty(string json) =>
        JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ApexPost.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
