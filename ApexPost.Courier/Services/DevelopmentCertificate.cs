#if DEBUG
using System.Net.Security;

namespace ApexPost.Courier.Services;

/// <summary>
/// DEBUG builds only. The Android emulator reaches the developer's machine as 10.0.2.2, which the ASP.NET Core
/// development certificate (issued to "localhost") does not name, and the emulator does not trust that certificate
/// anyway. This handler accepts that one certificate for local development hosts only. Every other certificate,
/// and every host in a Release build, gets the normal checks. See SECURITY.md.
/// </summary>
internal static class DevelopmentCertificate
{
    private static readonly string[] LocalHosts = ["localhost", "127.0.0.1", "10.0.2.2"];

    public static HttpMessageHandler CreateHandler() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, certificate, _, errors) =>
            errors == SslPolicyErrors.None
            || (request.RequestUri is { } uri
                && LocalHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
                && certificate?.Subject == "CN=localhost"),
    };
}
#endif
