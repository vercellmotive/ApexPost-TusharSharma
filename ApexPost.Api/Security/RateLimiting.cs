using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ApexPost.Api.Security;

/// <summary>The request limits, from the <c>RateLimiting</c> section. Each is per caller address per window.</summary>
public sealed class RateLimitOptions
{
    /// <summary>The configuration section.</summary>
    public const string Section = "RateLimiting";

    /// <summary>Requests per minute to any endpoint.</summary>
    public int GeneralPerMinute { get; set; } = 600;

    /// <summary>Requests per minute to the partner route. Tracking numbers are sequential, so a key must not be able to walk through them.</summary>
    public int PartnerPerMinute { get; set; } = 30;
}

internal static class RateLimiting
{
    public const string PartnerPolicy = "partner";

    public static IServiceCollection AddApexPostRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(Caller(context), _ => Window(limits.GeneralPerMinute)));

            // The partner route has its own, lower limit. It is checked before the API key, so repeated wrong keys
            // are throttled too.
            options.AddPolicy(PartnerPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter("partner:" + Caller(context), _ => Window(limits.PartnerPerMinute)));

            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await ApiSecurity.WriteProblemAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "Too many requests",
                    "Too many requests. Wait a little and try again.");
            };
        });
        return services;
    }

    private static string Caller(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions Window(int permits) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    };
}
