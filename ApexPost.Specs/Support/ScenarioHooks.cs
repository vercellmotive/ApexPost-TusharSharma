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
