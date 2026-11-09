using System.Text.Json;
using System.Text.Json.Serialization;

namespace ApexPost.Client;

// Hand-written half of the generated ApexPostClient. NSwag leaves these partial hooks for exactly this purpose,
// so the generated file never needs editing.
public partial class ApexPostClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        // Match the API: camelCase names, enums as names, and tolerate a property name in another case.
        settings.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        settings.PropertyNameCaseInsensitive = true;
        settings.Converters.Add(new JsonStringEnumConverter());
    }
}
