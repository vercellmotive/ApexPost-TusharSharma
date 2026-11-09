using ApexPost.Api.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ApexPost.Api.Security;

/// <summary>JWT bearer authentication, the authorization defaults and the partner API keys.</summary>
internal static class ApiSecurity
{
    public static IServiceCollection AddApexPostSecurity(this IServiceCollection services)
    {
        services.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Section).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<PartnerApiKeyOptions>().BindConfiguration(PartnerApiKeyOptions.Section);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwt) =>
            {
                // Keep the claim names as they are in the token ("role", "courierId"), without remapping.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.Value.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    RoleClaimType = ApexPostRoles.RoleClaim,
                    NameClaimType = "sub",
                };
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = WriteUnauthorizedAsync,
                    OnForbidden = context => WriteProblemAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "Your role does not allow this action."),
                };
            });

        // Every endpoint needs a signed-in user unless it says otherwise. Roles are added per action.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build());
        return services;
    }

    // 401 as ProblemDetails, saying whether the token was missing, expired or otherwise invalid. The token itself
    // is never written to the response or the log.
    private static Task WriteUnauthorizedAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var (error, detail) = context.AuthenticateFailure switch
        {
            null => (null, "This endpoint needs a bearer access token in the Authorization header."),
            SecurityTokenExpiredException => ("invalid_token", "The access token has expired. Sign in again."),
            _ => ("invalid_token", "The access token is not valid."),
        };
        context.Response.Headers.WWWAuthenticate = error is null ? "Bearer" : $"Bearer error=\"{error}\"";
        return WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized", detail);
    }

    internal static async Task WriteProblemAsync(HttpContext httpContext, int status, string title, string detail)
    {
        httpContext.Response.StatusCode = status;
        var problems = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }

    public static WebApplication UseApexPostTransportSecurity(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            // Browsers remember to use HTTPS only. Not in Development, where it would stick to localhost.
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        return app;
    }
}
