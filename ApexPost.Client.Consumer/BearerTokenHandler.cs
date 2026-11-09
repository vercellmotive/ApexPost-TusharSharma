using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

namespace ApexPost.Client.Consumer;

/// <summary>
/// Adds "Authorization: Bearer" to every call. The token comes from ApexPostApi:AccessToken (set it in the
/// ApexPostApi__AccessToken environment variable, never in appsettings.json). Without one, it asks the API's
/// Development-only /dev/token endpoint for a short-lived Admin and Courier token. The token is never logged.
/// </summary>
public sealed class BearerTokenHandler(IConfiguration configuration) : DelegatingHandler
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(request.RequestUri!, cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<string> GetTokenAsync(Uri requestUri, CancellationToken cancellationToken)
    {
        if (_token is not null)
        {
            return _token;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _token ??= configuration["ApexPostApi:AccessToken"] is { Length: > 0 } configured
                ? configured
                : await RequestDevelopmentTokenAsync(new Uri(requestUri.GetLeftPart(UriPartial.Authority)), cancellationToken);
            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> RequestDevelopmentTokenAsync(Uri apiRoot, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(apiRoot, "/dev/token"))
        {
            Content = JsonContent.Create(new { roles = new[] { "Admin", "Courier" }, courierId = "CR-1001" }),
        };
        using var response = await base.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Could not get a development token ({(int)response.StatusCode}). Run the API in Development or set ApexPostApi:AccessToken.");
        }

        var body = await response.Content.ReadFromJsonAsync<DevelopmentToken>(cancellationToken);
        return body?.AccessToken ?? throw new InvalidOperationException("The token response had no access token.");
    }

    private sealed record DevelopmentToken(string AccessToken);
}
