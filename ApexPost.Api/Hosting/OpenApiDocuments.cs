using ApexPost.Tracking.Domain;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ApexPost.Api.Hosting;

/// <summary>
/// One OpenAPI document per API version, made by the built-in generator from the API Explorer groups. There is no
/// second generator: Swagger UI only displays these documents.
/// </summary>
internal static class OpenApiDocuments
{
    public static IServiceCollection AddApexPostOpenApi(this IServiceCollection services)
    {
        foreach (var (documentName, _, deprecated) in ApiVersions.Documents)
        {
            services.AddOpenApi(documentName, options =>
            {
                // OpenAPI 3.0 rather than the default 3.1, because the NSwag client generator reads 3.0 fully.
                options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
                options.ShouldInclude = description => description.GroupName == documentName;
                options.AddDocumentTransformer(new DocumentInfoTransformer(documentName, deprecated));
                options.AddOperationTransformer<OperationTransformer>();
                options.AddSchemaTransformer(new EnumSchemaTransformer(documentName));
                options.AddDocumentTransformer<RequiredRequestFieldsTransformer>();
            });
        }

        return services;
    }

    /// <summary>Sets the title, description, version and contact of a document.</summary>
    internal sealed class DocumentInfoTransformer(string documentName, bool deprecated) : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            var description = "Track ApexPost shipments and upload the scans couriers record offline. "
                + "Every error response is ProblemDetails (application/problem+json).";
            if (deprecated)
            {
                description += $" Version 1 is deprecated and will stop on {ApiVersions.V1Sunset:yyyy-MM-dd}. Move to version 2.";
            }

            document.Info = new OpenApiInfo
            {
                Title = "ApexPost Logistics API",
                Version = documentName,
                Description = description,
                Contact = new OpenApiContact
                {
                    Name = "ApexPost Platform Team",
                    Email = "platform-team@apexpost.example",
                    Url = new Uri("https://developer.apexpost.example"),
                },
            };

            // No server list: the document is the same on every machine, so the committed contract can be compared
            // with it. Swagger UI and generated clients use the address they were given.
            document.Servers = [];
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Describes the enums the same way wherever they appear. The generator otherwise takes the shape from the first
    /// property that uses an enum, which can add null as a value. In v1 the shipment status is a number and in v2 a
    /// name; the scan type is a name in both.
    /// </summary>
    internal sealed class EnumSchemaTransformer(string documentName) : IOpenApiSchemaTransformer
    {
        public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
        {
            var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
            if (type == typeof(ShipmentStatus) && documentName == "v1")
            {
                schema.Type = JsonSchemaType.Integer;
                schema.Format = "int32";
                schema.Enum = [.. Enum.GetValues<ShipmentStatus>().Select(value => (System.Text.Json.Nodes.JsonNode)(int)value)];
                schema.Description = string.Join(", ", Enum.GetValues<ShipmentStatus>().Select(value => $"{(int)value} = {value}"));
            }
            else if (type == typeof(ShipmentStatus) || type == typeof(ScanType))
            {
                schema.Type = JsonSchemaType.String;
                schema.Format = null;
                schema.Enum = [.. Enum.GetNames(type).Select(name => (System.Text.Json.Nodes.JsonNode)name)];
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Request DTO properties are nullable so that a missing value fails [Required] instead of becoming 0. In the
    /// document a required request field must not be null, so the null option is removed there. Responses keep
    /// theirs: a required response field such as latestScan is always present but can be null. This runs on the
    /// finished document, after the generator has written nullable references as "oneOf null or the reference".
    /// </summary>
    internal sealed class RequiredRequestFieldsTransformer : IOpenApiDocumentTransformer
    {
        private static readonly HashSet<string> RequestSchemas =
        [
            nameof(Contracts.CreateShipmentRequest),
            nameof(Contracts.UpdateShipmentRequest),
            nameof(Contracts.UpdateStatusRequest),
            nameof(Contracts.ScanUploadRequest),
            nameof(Contracts.ScanUploadItem),
        ];

        public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            foreach (var (schemaName, schema) in document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
            {
                if (!RequestSchemas.Contains(schemaName) || schema is not OpenApiSchema { Properties: { } properties, Required: { } required })
                {
                    continue;
                }

                foreach (var name in required)
                {
                    switch (properties[name])
                    {
                        case OpenApiSchema { OneOf.Count: > 0 } wrapper when wrapper.OneOf.OfType<OpenApiSchemaReference>().SingleOrDefault() is { } reference:
                            properties[name] = reference;
                            break;
                        case OpenApiSchema { Type: { } propertyType } property:
                            property.Type = propertyType & ~JsonSchemaType.Null;
                            break;
                    }
                }
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Marks the operations of a deprecated version, and gives the tracking number its format.</summary>
    internal sealed class OperationTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            if (context.Description.IsDeprecated)
            {
                operation.Deprecated = true;
            }

            // The route constraint is invisible to the generator, so the pattern is added here.
            foreach (var parameter in operation.Parameters ?? [])
            {
                if (parameter is OpenApiParameter { Name: "trackingNumber", Schema: OpenApiSchema schema } named)
                {
                    schema.Pattern = TrackingNumber.Pattern;
                    named.Description ??= "The tracking number, APX- followed by eight digits.";
                }
            }

            return Task.CompletedTask;
        }
    }
}
