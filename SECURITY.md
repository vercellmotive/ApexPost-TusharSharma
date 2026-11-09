# ApexPost API security

## Authentication design

`ApexPost.Api` accepts two kinds of credential, each on its own routes.

| Who | Credential | Routes | Configured in |
|---|---|---|---|
| Couriers and back-office staff | JWT bearer token (`Authorization: Bearer …`) | Every `/api/v1` and `/api/v2` shipment and scan route | `Security/ApiSecurity.cs` |
| Partner systems | API key (`X-Api-Key: …`) | `GET /api/v2/partner/shipments/{trackingNumber}` only, read-only | `Security/PartnerEndpoints.cs`, `Security/PartnerApiKeyFilter.cs` |

**Token validation.** `AddAuthentication().AddJwtBearer()` checks four things on every token: the issuer (`Jwt:Issuer`), the audience (`Jwt:Audience`), the lifetime (`exp`, with 30 seconds of clock skew), and the HMAC-SHA256 signature (`Jwt:SigningKey`). Only HS256 is accepted, so a token cannot switch to another algorithm or to `none`. The issuer and audience are in `appsettings.json`. The signing key is a secret: it is read from user secrets and never committed. The app refuses to start if the key is missing or shorter than 32 bytes.

**Roles and claims.** A token carries `role` (`Courier`, `Admin` or both) and, for a courier, `courierId`.

| Endpoint | Rule |
|---|---|
| `GET` shipments, a shipment, scan history | Any signed-in user (the fallback policy) |
| `POST /scans` (scan upload), `PATCH /status` | `[Authorize(Roles = "Courier")]` |
| `POST` and `PUT` shipments | `[Authorize(Roles = "Admin")]` (counter staff create and correct shipments) |
| `DELETE` shipment | `[Authorize(Roles = "Admin")]` |

A courier may upload only scans recorded under their own `courierId` claim. `ShipmentService.UploadScansAsync` checks every scan in the batch against the claim, and one foreign scan refuses the whole batch with 403. A Courier token without a `courierId` claim cannot upload at all.

**401 and 403.** A missing, invalid or expired token returns 401 ProblemDetails with a `WWW-Authenticate: Bearer` challenge, and the detail says which (for example "The access token has expired. Sign in again."). The wrong role returns 403 ProblemDetails. The token itself is never written to a response or a log. `SecurityTests` covers each case.

**Development tokens.** `POST /dev/token` issues a short-lived token signed with the same key, and anyone who could call it could become an Admin, so it is guarded three times and all three must hold:

1. `ASPNETCORE_ENVIRONMENT` is Development. One wrong environment setting is not enough, because
2. `Jwt:AllowDevelopmentTokens` must also be `true`. It is set only in `appsettings.Development.json`, and the project leaves that file out of `dotnet publish`, so a published build has no such flag even if someone sets the environment to Development. A Production deployment has no such route.
3. The caller must be on the same machine (a loopback address). Anyone else gets a 404, as if the route did not exist. A request with no caller address at all, such as one arriving through a Unix-socket proxy, counts as remote, not local (`A_request_with_no_caller_address_does_not_get_a_development_token`).

`AllowedHosts` is `api.apexpost.example;localhost` in `appsettings.json` (and `*` only in Development, so the Android emulator's `10.0.2.2` works), so a forged `Host` header is rejected before it can reach a `Location` header. Replace the host name with the real one when deploying. Rate limits are per caller address; behind a reverse proxy, configure forwarded headers so each caller keeps their own limit. Swagger UI keeps the pasted token in the browser's local storage in Development.

Setting the environment wrongly does expose the other Development features (Swagger UI, full error details, automatic migrations and seeding), so deployments must set the environment explicitly. `dotnet user-jwts` is the alternative the brief names, and it needs none of this: it keeps the signing key in user secrets and issues tokens with the .NET CLI.

```bash
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 32)" --project ApexPost.Api
```

```bash
curl -k -X POST https://localhost:7180/dev/token -H "Content-Type: application/json" -d '{"roles":["Courier"],"courierId":"CR-1001"}'
```

It is not mapped outside Development, and it is not in the OpenAPI documents. `SecurityTests` checks that production never issues a token, that the setting is needed, and that only loopback callers are served. In Swagger UI, paste its `accessToken` into **Authorize → Bearer**, and every "Try it out" sends it.

## Partner API keys

Keys are configured per partner in the `PartnerApiKeys:Keys` section, from user secrets in development and the host's secret store in production:

```bash
dotnet user-secrets set "PartnerApiKeys:Keys:DemoPartner" "$(openssl rand -hex 24)" --project ApexPost.Api
```

`appsettings.json` holds an empty `Keys` object, so with no secrets configured every key fails. The endpoint filter compares SHA-256 hashes of the supplied key and each configured key with `CryptographicOperations.FixedTimeEquals`, and checks every key, so the time taken reveals nothing. Keys shorter than 32 characters are ignored. A missing or wrong key returns 401 ProblemDetails, and a bearer token does not help on this route. Only the partner's name is logged, never the key. The partner response leaves out the recipient's name, phone and address.

## Rate limiting

`UseRateLimiter()` runs before authentication, so a flood of bad tokens or keys is turned away cheaply. Two fixed one-minute windows per caller address, set in the `RateLimiting` section: 600 requests for any endpoint, and 30 for the partner route. The partner limit is checked before the API key, so repeated wrong keys are throttled too, and a key cannot walk through the sequential tracking numbers quickly. A refused request gets 429 ProblemDetails with a `Retry-After` header. `SecurityTests` covers both limits. Behind a reverse proxy, the caller address is the proxy's unless forwarded headers are configured, so production needs `UseForwardedHeaders` with the proxy listed, as in the Week 3 web app.

## What a courier can see

By design, any signed-in user, including a Courier, can read every shipment, with the recipient's name, phone and address, and a Courier can change the status of any shipment. The brief asks for exactly that, so it is how the API works. It is a known limit, not an oversight. To narrow it, assign each shipment to a courier (a `courierId` on the shipment), filter the list and reads for the Courier role by that claim, and check it in `ChangeStatusAsync` the way `UploadScansAsync` checks scans. The partner route already returns no personal data.

## OAuth 2.0 flows

The development tokens stand in for a real **identity provider** (IdP), such as Microsoft Entra ID, Auth0 or Keycloak. The IdP signs people and systems in and issues tokens. The API only validates them.

**Authorization Code with PKCE, for the courier app.** The app is a public client: it cannot keep a client secret, because anything in an app package can be extracted.

1. The app makes a random `code_verifier` and sends its SHA-256 hash, the `code_challenge`, when it opens the IdP's sign-in page in the system browser.
2. The courier signs in there, with multi-factor authentication if the IdP requires it. The app never sees the password.
3. The IdP redirects back to the app with a one-time authorization `code`.
4. The app swaps the code and the original `code_verifier` for tokens. The IdP checks that the verifier hashes to the challenge, so a code stolen from the redirect is useless to anyone else.

**Client Credentials, for partner systems.** A partner's server is a confidential client: it can keep a secret, and there is no user. It authenticates to the IdP with its client id and a secret or certificate, receives an access token for the ApexPost audience with a partner scope, and calls the API with that token. Nobody signs in interactively.

**The tokens.**

- **Access token (a JWT).** Short-lived, for example 10 to 60 minutes. It names the issuer, audience, subject, expiry and claims such as roles and `courierId`, and it is signed by the IdP. The API checks the signature and claims locally on every request without calling the IdP. Because it is a bearer token, whoever holds it can use it, so it must stay short-lived and travel only over HTTPS.
- **Refresh token.** Long-lived, given only to the app, and sent only to the IdP, never to the API. When the access token expires, the app swaps the refresh token for a new access token without asking the courier to sign in again. The IdP can revoke it, which signs a lost device out, and should rotate it on each use.

**Why API keys are weaker than OAuth 2.0 tokens.** An API key is a static shared secret. It does not expire on its own, so a leaked key works until someone notices and rotates it. It carries no scopes or claims, so it identifies a partner but cannot limit what that partner may do. It is sent on every request and is often stored in configuration files, which gives it many chances to leak. Revoking it means coordinating a new key with the partner. An OAuth access token is short-lived, scoped, signed and issued by a central IdP that can revoke the client, and the client's own secret is sent only to the IdP. That is why the partner route here is read-only and returns no personal data.

**Moving to a real identity provider.**

1. Register the API in the IdP with an audience such as `api://apexpost`, and define app roles `Courier` and `Admin` plus a `courierId` claim, or map existing directory attributes to them.
2. In `ApiSecurity`, set `options.Authority` to the IdP's issuer URL and drop `IssuerSigningKey`. JwtBearer then downloads the IdP's signing keys from its OpenID Connect metadata and follows key rotation. Keep issuer, audience and lifetime validation, change `ValidAlgorithms` to the IdP's (usually RS256), and remove `Jwt:SigningKey` from configuration.
3. Delete `DevelopmentTokens.cs` and the `/dev/token` route.
4. In the MAUI app, replace the Login page with `WebAuthenticator` (or MSAL) running Authorization Code with PKCE. Store the access and refresh tokens in SecureStorage, and refresh in `AuthHeaderHandler` before the access token expires.
5. Register each partner as a confidential client for Client Credentials. Accept their tokens on the partner route alongside API keys, and retire the keys partner by partner.

## Transport security: HTTPS and HSTS

`UseHttpsRedirection()` runs in every environment, and `UseHsts()` runs outside Development (`ApiSecurity.UseApexPostTransportSecurity`). HSTS tells browsers to use only HTTPS for the host from then on. It is left out of Development so it does not stick to `localhost`, and ASP.NET Core never sends it to localhost anyway. `SecurityTests` checks both.

A bearer token or an API key is a complete credential: whoever has it is the courier or the partner, with no password or second factor needed. Over plain HTTP, anyone on the path can read it, including public Wi-Fi, a compromised router, a proxy or the mobile network. They can then replay it, and a server cannot tell the difference. TLS encrypts the request headers, so the `Authorization` and `X-Api-Key` values are protected in transit, and the server's certificate proves the app is talking to the real API. A redirect from HTTP is only a backstop: a client that sent its token over HTTP has already leaked it before the redirect, which is why the app and the client SDK are configured with `https://` addresses only.

## Development certificate in the courier app (`#if DEBUG` only)

On a developer's machine the API uses the ASP.NET Core development certificate, which is issued to `localhost`. The Android emulator reaches the host as `10.0.2.2`, a name the certificate does not cover, and the emulator does not trust that certificate. So `ApexPost.Courier/Services/DevelopmentCertificate.cs` installs a certificate callback that:

- exists only when `DEBUG` is defined, because the whole file and its registration in `MauiProgram` are inside `#if DEBUG`, so a Release build has no bypass;
- accepts a certificate that fails normal checks only when the host is `localhost`, `127.0.0.1` or `10.0.2.2` **and** the certificate's subject is `CN=localhost`. This does not pin one certificate: any self-signed certificate with that subject passes, which is acceptable only because it is Debug-only and for local hosts. To tighten it, compare the certificate's thumbprint with the one from `dotnet dev-certs https --check`;
- leaves every other host, and every certificate that passes normal checks, to the platform's own validation.

A Release build talks only to a production host with a certificate from a public authority, and never turns validation off. The NSwag console consumer and the test clients never bypass certificate checks.

## Release builds of the app

The "Get development token" button, the courier-id box, `LoginViewModel.RequestTokenAsync`, `ApexApiClient.RequestDevelopmentTokenAsync` and their DTOs exist only in Debug builds (`#if DEBUG`). A Release build has just the paste-a-token path, so it never ships a sign-in button that needs no password. The `DevelopmentCertificate` handler is likewise Debug-only.

## Backup on Android

`allowBackup` is `false`, with `data_extraction_rules.xml` excluding every domain from cloud backup and device transfer. The offline database holds scan locations and signature names unencrypted, and a SecureStorage token restored onto another device cannot be decrypted anyway. `SecureStorageTokenStore.GetAsync` also catches a failed decryption, forgets the value and returns no token, so the courier signs in again instead of the app failing.

## Token storage in the app

- The access token is stored only in MAUI `SecureStorage` (`SecureStorageTokenStore`): the Keychain on iOS and Mac Catalyst, the Android Keystore, and DPAPI on Windows. It is never put in Preferences, a file or a log.
- `AuthHeaderHandler` adds it to each request. On a 401 it clears the stored token and raises `SessionEvents.SignedOut`, and the app goes to the Login page.
- Logs record the method, path and status of each call, and retries and circuit changes. They never record a token, an API key or a request body. `ApiClientTests` checks that a token never appears in the log.

## Secrets in the repository

There are none. `appsettings.json` has an empty `Jwt:SigningKey` and empty `PartnerApiKeys:Keys`. The test project signs tokens with a test-only key derived in code, used only in memory by the tests. The demo values in `ApexPost.Api.http` are placeholders you replace with your own user secrets.
