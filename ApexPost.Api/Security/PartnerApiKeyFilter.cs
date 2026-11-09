using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ApexPost.Api.Security;

/// <summary>
/// The endpoint filter for partner routes. It accepts a request only with a configured key in the X-Api-Key header,
/// and answers 401 ProblemDetails otherwise. Keys are compared in constant time, and only the partner name is logged.
/// </summary>
internal sealed class PartnerApiKeyFilter(IOptionsMonitor<PartnerApiKeyOptions> options, ILogger<PartnerApiKeyFilter> logger) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";
    public const string PartnerItem = "ApexPost.Partner";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var supplied = httpContext.Request.Headers[HeaderName].ToString();
        var partner = string.IsNullOrEmpty(supplied) ? null : FindPartner(supplied);
        if (partner is null)
        {
            logger.LogWarning("Partner request to {Path} refused: {Reason}.", httpContext.Request.Path, string.IsNullOrEmpty(supplied) ? "no API key" : "unknown API key");
            httpContext.Response.Headers.WWWAuthenticate = "ApiKey";
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: $"This endpoint needs a valid partner API key in the {HeaderName} header.");
        }

        httpContext.Items[PartnerItem] = partner;
        logger.LogInformation("Partner {Partner} called {Path}.", partner, httpContext.Request.Path);
        return await next(context);
    }

    private string? FindPartner(string supplied)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        string? match = null;

        // Every key is compared, so the time taken does not reveal which partner, if any, came close.
        foreach (var (name, key) in options.CurrentValue.Keys)
        {
            if (key.Length >= PartnerApiKeyOptions.MinimumKeyLength
                && CryptographicOperations.FixedTimeEquals(suppliedHash, SHA256.HashData(Encoding.UTF8.GetBytes(key))))
            {
                match = name;
            }
        }

        return match;
    }
}
