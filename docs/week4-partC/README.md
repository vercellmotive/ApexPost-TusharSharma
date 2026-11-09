# Week 4 Part C evidence

All screenshots are of the real Swagger UI served by the secured `ApexPost.Api` in Development over HTTPS (`https://localhost:7180`), taken with headless Chrome by [`tools/swagger-screenshots.mjs`](tools/swagger-screenshots.mjs). The data is fictional. They were retaken after Part D, so they show the Authorize button, the bearer token header and the 401 response. Tokens are masked in the pages before capture, so no credential is in an image.

| File | Shows |
|---|---|
| [`swagger-authorize.png`](swagger-authorize.png) | The Authorize dialog after pasting a development token: the Bearer scheme is "Authorized" (value hidden) and the partner ApiKey scheme is offered below it |
| [`response-401-unauthorized.png`](response-401-unauthorized.png) | "Try it out" on `GET /api/v2/shipments` before authorizing: 401 ProblemDetails with `www-authenticate: Bearer` |
| [`swagger-v2.png`](swagger-v2.png) | Swagger UI with the "v2" document selected: the title, description, contact, both tags and every operation |
| [`swagger-v1.png`](swagger-v1.png) | The "v1 (deprecated)" document: every operation is struck through as deprecated, and the description gives the sunset date |
| [`response-201-location.png`](response-201-location.png) | "Try it out" on `POST /api/v2/shipments`: 201 Created with the `location` header and the version headers |
| [`response-400-validation.png`](response-400-validation.png) | The same request with bad fields: 400 `ValidationProblemDetails` naming each field |
| [`response-409-conflict.png`](response-409-conflict.png) | The 201 request sent again: 409 Conflict for the duplicate tracking number |
| [`http-check.txt`](http-check.txt) | The Part C run of the 44 requests in `ApexPost.Api.http` (before security; the Part D run with 60 requests and tokens is in `../week4-partD/http-check.txt`) sent by [`tools/check-http.py`](tools/check-http.py), each returning the status its title states |
| [`consumer-output.txt`](consumer-output.txt) | The output of `ApexPost.Client.Consumer`: create, fetch, upload two scans, handle a 404 |

Headless Chrome does not trust the ASP.NET Core development certificate, so the screenshot script starts it with `--ignore-certificate-errors`. That flag affects only that throwaway browser profile. The API and the app never skip certificate checks.

To take them again, start the API in Development over HTTPS with fresh data (see the Part D section of the README for the two user secrets), then:

```bash
node docs/week4-partC/tools/swagger-screenshots.mjs https://localhost:7180 docs/week4-partC
```
