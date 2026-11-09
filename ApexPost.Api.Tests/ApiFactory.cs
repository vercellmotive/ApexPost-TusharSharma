using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using ApexPost.Api.Security;
using ApexPost.Application.Interfaces;
using ApexPost.Tracking.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Tests;

/// <summary>
/// The real API pipeline with the store, the Week 3 branches and the clock replaced by in-memory doubles. Clients from
/// <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/> send a token for courier CR-1001 who is also an
/// Admin; <see cref="CreateAnonymousClient"/> sends none.
/// </summary>
public sealed class ApiFactory(string environment = "Testing") : WebApplicationFactory<Program>
{
    // Test-only values. They sign tokens in memory and are never used outside these tests.
    public const string Issuer = "https://identity.test.example";
    public const string Audience = "apexpost-api-tests";
    public static readonly string SigningKey = Convert.ToBase64String(SHA256.HashData("apexpost-test-signing-key"u8));
    public const string PartnerKey = "test-partner-key-0123456789abcdef0123";

    public static void UseTestSecurity(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("PartnerApiKeys:Keys:TestPartner", PartnerKey);
    }

    /// <summary>A token signed with the test key. Change the issuer, audience, key or expiry to make a bad one.</summary>
    public static string Token(
        string[] roles,
        string? courierId = Make.CourierId,
        DateTimeOffset? expires = null,
        string issuer = Issuer,
        string audience = Audience,
        string? signingKey = null)
    {
        var claims = new List<Claim> { new("sub", courierId ?? "admin") };
        claims.AddRange(roles.Select(role => new Claim(ApexPostRoles.RoleClaim, role)));
        if (courierId is not null)
        {
            claims.Add(new Claim(ApexPostRoles.CourierIdClaim, courierId));
        }

        var end = expires ?? DateTimeOffset.UtcNow.AddMinutes(30);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = end.AddHours(-2).UtcDateTime,
            IssuedAt = end.AddHours(-2).UtcDateTime,
            Expires = end.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(signingKey ?? SigningKey)), SecurityAlgorithms.HmacSha256),
        });
    }

    public static string Token(params string[] roles) => Token(roles, Make.CourierId);

    public HttpClient CreateAnonymousClient() => base.CreateClient();

    public HttpClient CreateClient(string token)
    {
        var client = base.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public new HttpClient CreateClient() => CreateClient(Token(ApexPostRoles.Admin, ApexPostRoles.Courier));

    public InMemoryShipmentStore Store { get; } = new();
    public TestClock Clock { get; } = new();

    /// <summary>When set, every store call throws this, to exercise the global exception handler.</summary>
    public Exception? StoreFailure { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("Tracking:PrepareDevelopmentDatabase", "false");
        UseTestSecurity(builder);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IShipmentStore>(_ => StoreFailure is null ? Store : new FailingStore(StoreFailure));
            services.AddSingleton<IBranchRepository, FakeBranches>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }

    private sealed class FailingStore(Exception failure) : IShipmentStore
    {
        public Task<PagedResult<ShipmentView>> ListAsync(ShipmentQuery query, CancellationToken ct = default) => throw failure;
        public Task<ShipmentView?> GetViewAsync(string trackingNumber, CancellationToken ct = default) => throw failure;
        public Task<Tracking.Domain.Shipment?> FindAsync(string trackingNumber, CancellationToken ct = default) => throw failure;
        public Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default) => throw failure;
        public void Add(Tracking.Domain.Shipment shipment) => throw failure;
        public void Remove(Tracking.Domain.Shipment shipment) => throw failure;
        public Task SaveChangesAsync(CancellationToken ct = default) => throw failure;
        public Task<IReadOnlyList<Tracking.Domain.TrackingScan>> GetScansAsync(int shipmentId, CancellationToken ct = default) => throw failure;
        public Task<IReadOnlyDictionary<Guid, int>> FindScanShipmentsAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken ct = default) => throw failure;
        public Task<IReadOnlySet<Guid>> AddScansAsync(IReadOnlyList<Tracking.Domain.TrackingScan> scans, CancellationToken ct = default) => throw failure;
    }
}
