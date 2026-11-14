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
