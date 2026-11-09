using System.Text;
using System.Text.Json;

namespace ApexPost.Courier.Core.Auth;

/// <summary>
/// Reads the courierId claim from the stored token, so new scans are recorded under the courier the server knows.
/// The app does not validate the token; the server does that on every call.
/// </summary>
public static class CourierIdentity
{
    public static string? CourierIdFrom(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return json.RootElement.TryGetProperty("courierId", out var claim) && claim.ValueKind == JsonValueKind.String
                ? claim.GetString()
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }
}
