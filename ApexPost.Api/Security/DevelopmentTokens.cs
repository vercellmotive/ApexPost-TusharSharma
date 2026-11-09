using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Security;

/// <summary>A request for a development token.</summary>
/// <param name="Roles">Courier, Admin or both.</param>
/// <param name="CourierId">The courier id for a Courier token, such as CR-1001.</param>
public sealed record DevelopmentTokenRequest(IReadOnlyList<string>? Roles, string? CourierId);

/// <summary>A development access token.</summary>
/// <param name="AccessToken">The signed JWT. Send it as "Authorization: Bearer {token}".</param>
/// <param name="TokenType">Always Bearer.</param>
/// <param name="ExpiresAtUtc">When the token stops working.</param>
public sealed record DevelopmentTokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// POST /dev/token issues short-lived tokens for testing, signed with the same key the API validates with. Three
/// separate guards must all hold, so one wrong setting does not hand out Admin tokens: the environment must be
/// Development, <c>Jwt:AllowDevelopmentTokens</c> must be true (it is set only in appsettings.Development.json), and
/// the caller must be on the same machine. It is not in the OpenAPI documents. A real identity provider replaces
/// it; see SECURITY.md.
/// </summary>
internal static class DevelopmentTokens
{
    public static void MapDevelopmentTokens(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue<bool>(AllowSetting))
        {
            return;
        }

        app.MapPost("/dev/token", Issue).AllowAnonymous().ExcludeFromDescription();
    }

    /// <summary>The configuration switch that turns the endpoint on. Off unless a developer's own settings say so.</summary>
    public const string AllowSetting = "Jwt:AllowDevelopmentTokens";

    /// <summary>Kestrel always sets the caller's address. A test host has none, and is in the same process.</summary>
    internal static bool IsLocalCaller(System.Net.IPAddress? address) => address is null || System.Net.IPAddress.IsLoopback(address);

    internal static IResult Issue(HttpContext httpContext, DevelopmentTokenRequest request, IOptions<JwtOptions> options)
    {
        if (!IsLocalCaller(httpContext.Connection.RemoteIpAddress))
        {
            // Not a developer's own machine: behave as if the route did not exist.
            return TypedResults.NotFound();
        }

        var roles = request.Roles ?? [];
        var errors = new Dictionary<string, string[]>();
        if (roles.Count == 0 || roles.Any(role => !ApexPostRoles.All.Contains(role)))
        {
            errors["roles"] = [$"Give one or more of: {string.Join(", ", ApexPostRoles.All)}."];
        }

        if (roles.Contains(ApexPostRoles.Courier) && string.IsNullOrWhiteSpace(request.CourierId))
        {
            errors["courierId"] = ["A Courier token needs the courier id it may upload scans under."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var jwt = options.Value;
        // The real clock, because the bearer handler checks the lifetime against the real clock too.
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(jwt.DevelopmentTokenMinutes);
        var claims = new List<Claim> { new("sub", request.CourierId ?? "dev-admin") };
        claims.AddRange(roles.Distinct().Select(role => new Claim(ApexPostRoles.RoleClaim, role)));
        if (!string.IsNullOrWhiteSpace(request.CourierId))
        {
            claims.Add(new Claim(ApexPostRoles.CourierIdClaim, request.CourierId.Trim()));
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateSigningKey(), SecurityAlgorithms.HmacSha256),
        });
        return TypedResults.Ok(new DevelopmentTokenResponse(token, "Bearer", expires));
    }
}
