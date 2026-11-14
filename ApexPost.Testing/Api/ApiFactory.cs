extern alias api;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using ApexPost.Api.Security;
using ApexPost.Application.Interfaces;
using ApexPost.Tracking.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ApexPost.Testing.Tracking;
using Microsoft.Extensions.Time.Testing;

namespace ApexPost.Testing.Api;

/// <summary>
/// The real API pipeline with the store, the Week 3 branches and the clock replaced by in-memory doubles. Clients from
/// <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/> send a token for courier CR-1001 who is also an
/// Admin; <see cref="CreateAnonymousClient"/> sends none.
/// </summary>
public sealed class ApiFactory(string environment = "Testing") : WebApplicationFactory<api::Program>
{
    // Test-only values. The keys are generated for each test run, so no key is written in the code or committed.
    public const string Issuer = "https://identity.test.example";
    public const string Audience = "apexpost-api-tests";
    public static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public static readonly string PartnerKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    // Token lifetimes are fixed dates, not "now plus 30 minutes". The bearer handler checks them against the real
    // clock, so a token valid until 2099 is always valid and one that expired in 2021 is always expired.
    public static readonly DateTimeOffset ValidUntil = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset ExpiredAt = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset IssuedAt = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

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

        var end = expires ?? ValidUntil;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = IssuedAt.UtcDateTime,
            IssuedAt = IssuedAt.UtcDateTime,
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
    public FakeTimeProvider Clock { get; } = TestTime.NewClock();

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
            services.AddTransient<IStartupFilter, LoopbackCallerFilter>();
        });
    }

    /// <summary>The test host has no caller address. This gives requests a loopback one, unless a test asks for none.</summary>
    public const string NoAddressHeader = "X-Test-No-Address";

    private sealed class LoopbackCallerFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = context.Request.Headers.ContainsKey(NoAddressHeader) ? null : System.Net.IPAddress.Loopback;
                return nextMiddleware();
            });
            next(app);
        };
    }

    private sealed class FailingStore(Exception failure) : IShipmentStore
    {
        public Task<PagedResult<ShipmentView>> ListAsync(ShipmentQuery query, CancellationToken ct = default) => throw failure;
        public Task<ShipmentView?> GetViewAsync(string trackingNumber, CancellationToken ct = default) => throw failure;
        public Task<ApexPost.Tracking.Domain.Shipment?> FindAsync(string trackingNumber, CancellationToken ct = default) => throw failure;
        public Task<bool> ExistsAsync(string trackingNumber, CancellationToken ct = default) => throw failure;
        public void Add(ApexPost.Tracking.Domain.Shipment shipment) => throw failure;
        public void Remove(ApexPost.Tracking.Domain.Shipment shipment) => throw failure;
        public Task SaveChangesAsync(CancellationToken ct = default) => throw failure;
        public Task<IReadOnlyList<ApexPost.Tracking.Domain.TrackingScan>> GetScansAsync(int shipmentId, CancellationToken ct = default) => throw failure;
        public Task<IReadOnlyDictionary<Guid, int>> FindScanShipmentsAsync(IReadOnlyCollection<Guid> scanIds, CancellationToken ct = default) => throw failure;
        public Task<IReadOnlySet<Guid>> AddScansAsync(IReadOnlyList<ApexPost.Tracking.Domain.TrackingScan> scans, CancellationToken ct = default) => throw failure;
    }
}
