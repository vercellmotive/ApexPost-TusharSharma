# Prompt 2: a Gherkin feature and its step definitions

Sent to GitHub Copilot CLI 1.0.112 (`copilot -p`, Copilot Free plan, model chosen automatically, no tools allowed). The exact prompt:

````text
Write a Gherkin feature file named AccessControl.feature and its Reqnroll 3 (xUnit) step definitions in C# for the access rules of the ApexPost shipment API v2. Use business language in the feature, one Background, and at least one Scenario Outline with an Examples table. Tag the feature @api so the existing hooks below start the API. Share state through the injected ApiScenarioContext shown below, never through static fields.

The rules:
- Reading a shipment (GET /api/v2/shipments/{trackingNumber}) needs any signed-in user: no token gives 401.
- Deleting a shipment (DELETE /api/v2/shipments/{trackingNumber}) needs the Admin role: a Courier gets 403. An Admin gets 204 while the shipment is still Created.
- Changing the status (PATCH /api/v2/shipments/{trackingNumber}/status with JSON {"status":"PickedUp"}) and uploading scans (POST /api/v2/shipments/{trackingNumber}/scans with JSON {"scans":[...]}) need the Courier role: an Admin without the Courier role gets 403.
- A courier may upload scans only under the courierId in their own token: a scan whose courierId is another courier's gives 403 and nothing is stored.
- The partner route GET /api/v2/partner/shipments/{trackingNumber} needs the X-Api-Key header: a missing or wrong key gives 401, the configured key gives 200.
- Every 401 and 403 body is ProblemDetails (content type application/problem+json).

Test helpers that already exist:
- context.Api is a SqliteApiFactory (a WebApplicationFactory). context.Api.CreateClient(params string[] roles) returns an HttpClient whose bearer token is for courier "CR-1001" with those roles. context.Api.CreateDefaultClient() returns a client with no token. context.Api.Database.CreateContext() returns the EF Core DbContext with DbSet<Shipment> Shipments and DbSet<TrackingScan> Scans.
- ApexPostRoles.Admin and ApexPostRoles.Courier are the role names (namespace ApexPost.Api.Security). ApiFactory.PartnerKey is the configured partner key (namespace ApexPost.Testing.Api).
- A shipment is created by an Admin with POST /api/v2/shipments and JSON {"trackingNumber":"APX-70000001","originBranchCode":"B001","recipientName":"Mia Fictional","recipientPhone":"+61 2 5550 1234","recipientAddress":"12 Example Lane","city":"Sydney","weightKg":2.5}.
- A scan item is {"scanId":"scan::<lower-case guid>","scanType":"Pickup","courierId":"CR-1001","location":"Sydney depot","scannedAtUtc":"<ISO time>"}; the server's clock is context.Api.Clock (a FakeTimeProvider).

// ApexPost.Specs/Support/ApiScenarioContext.cs
using ApexPost.Testing.Api;

namespace ApexPost.Specs.Support;

/// <summary>
/// The state of one API-level scenario, shared between its step classes by Reqnroll's context injection. Reqnroll
/// creates one instance per scenario, so no state crosses from one scenario to the next and nothing is static.
/// </summary>
public sealed class ApiScenarioContext
{
    /// <summary>The API over its own SQLite database. Started and stopped by <see cref="ScenarioHooks"/>.</summary>
    public SqliteApiFactory Api { get; set; } = null!;

    public HttpClient Client { get; set; } = null!;

    public string OriginBranch { get; set; } = "B001";

    public HttpResponseMessage? LastResponse { get; set; }
}


// ApexPost.Specs/Support/ScenarioHooks.cs
using ApexPost.Testing.Api;
using ApexPost.Testing.Offline;
using Reqnroll;

namespace ApexPost.Specs.Support;

/// <summary>
/// Sets up and removes each scenario's test data. Every scenario gets its own API and database, or its own device
/// database, so scenarios can run in any order and in parallel.
/// </summary>
[Binding]
public sealed class ScenarioHooks(ApiScenarioContext api, ScanSyncContext sync)
{
    [BeforeScenario("api")]
    public void StartTheApiWithAnEmptyDatabase() => api.Api = new SqliteApiFactory();

    [AfterScenario("api")]
    public void StopTheApiAndDropItsDatabase()
    {
        api.LastResponse?.Dispose();
        api.Client?.Dispose();
        api.Api?.Dispose();
    }

    [BeforeScenario("device")]
    public void OpenAnEmptyDeviceDatabase() => sync.Device = new TestCourierDatabase();

    [AfterScenario("device")]
    public void DeleteTheDeviceDatabase() => sync.Device?.Dispose();
}


Reply with the feature file and one C# file, no explanation.
````
