using Asp.Versioning;

namespace ApexPost.Api.Hosting;

/// <summary>The API versions, their OpenAPI documents and the v1 sunset date. See VERSIONING.md.</summary>
internal static class ApiVersions
{
    public static readonly ApiVersion V1 = new(1, 0);
    public static readonly ApiVersion V2 = new(2, 0);

    /// <summary>When v1 was deprecated: the day v2 shipped. Clients get it in the Deprecation response header.</summary>
    public static readonly DateTimeOffset V1Deprecated = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The last day v1 is served. Clients get it in the Sunset response header.</summary>
    public static readonly DateTimeOffset V1Sunset = new(2027, 6, 30, 0, 0, 0, TimeSpan.Zero);

    public const string PolicyUrl = "https://github.com/vercellmotive/ApexPost-TusharSharma/blob/master/VERSIONING.md";

    /// <summary>The OpenAPI documents, by API Explorer group name, with the label Swagger UI shows for each.</summary>
    public static readonly IReadOnlyList<(string DocumentName, string Label, bool Deprecated)> Documents =
    [
        ("v2", "v2", false),
        ("v1", "v1 (deprecated)", true),
    ];

    public static IServiceCollection AddApexPostVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
            {
                // The version is part of the URL, /api/v2/shipments, so it is always explicit.
                options.ApiVersionReader = new UrlSegmentApiVersionReader();

                // Responses carry api-supported-versions and api-deprecated-versions headers.
                options.ReportApiVersions = true;

                // And, for v1, Deprecation and Sunset headers with the dates and a link to the policy.
                options.Policies.Deprecate(V1)
                    .Effective(V1Deprecated)
                    .Link(PolicyUrl)
                    .Title("ApexPost API versioning policy")
                    .Type("text/html");
                options.Policies.Sunset(V1)
                    .Effective(V1Sunset)
                    .Link(PolicyUrl)
                    .Title("ApexPost API versioning policy")
                    .Type("text/html");
            })
            .AddMvc()
            .AddApiExplorer(options =>
            {
                // Group names v1 and v2 are also the OpenAPI document names, so /openapi/v1.json holds v1.
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });
        return services;
    }
}
