# Week 4 Part C evidence

All screenshots are of the real Swagger UI served by `ApexPost.Api` in Development, taken with headless Chrome by [`tools/swagger-screenshots.mjs`](tools/swagger-screenshots.mjs). The data is fictional.

| File | Shows |
|---|---|
| [`swagger-v2.png`](swagger-v2.png) | Swagger UI with the "v2" document selected: the title, description, contact, both tags and every operation |
| [`swagger-v1.png`](swagger-v1.png) | The "v1 (deprecated)" document: every operation is struck through as deprecated, and the description gives the sunset date |
| [`response-201-location.png`](response-201-location.png) | "Try it out" on `POST /api/v2/shipments`: 201 Created with the `location` header and the version headers |
| [`response-400-validation.png`](response-400-validation.png) | The same request with bad fields: 400 `ValidationProblemDetails` naming each field |
| [`response-409-conflict.png`](response-409-conflict.png) | The 201 request sent again: 409 Conflict for the duplicate tracking number |
| [`http-check.txt`](http-check.txt) | Every request in `ApexPost.Api.http` (44, both versions) sent by [`tools/check-http.py`](tools/check-http.py), each returning the status its title states |
| [`consumer-output.txt`](consumer-output.txt) | The output of `ApexPost.Client.Consumer`: create, fetch, upload two scans, handle a 404 |

The development certificate is not trusted on the machine that took these, so for the screenshots and the two text files the API was started on plain HTTP on localhost (`--urls http://localhost:5180`). The code and the HTTPS profile are unchanged.

To take them again, start the API in Development with fresh data, then:

```bash
node docs/week4-partC/tools/swagger-screenshots.mjs http://localhost:5180 docs/week4-partC
```
