using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;

namespace ApexPost.Courier.Core.Auth;

/// <summary>
/// Adds the bearer token from secure storage to every request. On a 401 it clears the stored token and raises
/// <see cref="SessionEvents.SignedOut"/>, which sends the courier to the Login page. The token is never logged.
/// </summary>
public sealed class AuthHeaderHandler(ITokenStore tokens, SessionEvents session, ILogger<AuthHeaderHandler> logger) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(token))
        {
            logger.LogWarning("The server refused the access token for {Path}. Signing out.", request.RequestUri?.AbsolutePath);
            await tokens.ClearAsync();
            session.RaiseSignedOut();
        }

        return response;
    }
}
