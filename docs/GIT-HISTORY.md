# Git history

Repository: <https://github.com/vercellmotive/ApexPost-TusharSharma> (earlier pull requests were opened as `vercellmotive/ApexPost`, and the old address redirects, so their links still work). A zip leaves out `.git`, so this snapshot shows how the work was done: Weeks 2 and 3 up to PR #15, and Week 4 in the last section. It can't list the commit that updates it, so GitHub is the full record.

## Pull requests

| PR | Title | State |
|---|---|---|
| [#1](https://github.com/vercellmotive/ApexPost/pull/1) | Address Week 2 assessment remarks | MERGED |
| [#2](https://github.com/vercellmotive/ApexPost/pull/2) | Close remaining Week 2 assessment gaps | MERGED |
| [#3](https://github.com/vercellmotive/ApexPost/pull/3) | Move persistence to SQL Server with EF Core 10 | MERGED |
| [#4](https://github.com/vercellmotive/ApexPost/pull/4) | Add SQL Server evidence and replace the low-stock index | MERGED |
| [#5](https://github.com/vercellmotive/ApexPost/pull/5) | Close the remaining Week 3 assessment gaps | MERGED |
| [#6](https://github.com/vercellmotive/ApexPost/pull/6) | Close the final Week 3 assessment gaps | MERGED |
| [#7](https://github.com/vercellmotive/ApexPost/pull/7) | Close the gaps from the 91/100 review | MERGED |
| [#8](https://github.com/vercellmotive/ApexPost/pull/8) | Explain the W2 estimate mismatch | MERGED |
| [#9](https://github.com/vercellmotive/ApexPost/pull/9) | Close three small gaps from the wider checks | MERGED |
| [#10](https://github.com/vercellmotive/ApexPost/pull/10) | Close the gaps from grading against the brief | MERGED |
| [#11](https://github.com/vercellmotive/ApexPost/pull/11) | Return Problem Details from action errors | MERGED |
| [#12](https://github.com/vercellmotive/ApexPost/pull/12) | Make the README setup work from a clean unzip | MERGED |
| [#13](https://github.com/vercellmotive/ApexPost/pull/13) | Close the remaining remarks from the brief | MERGED |
| [#14](https://github.com/vercellmotive/ApexPost/pull/14) | Close the last gaps from the score card | MERGED |
| [#15](https://github.com/vercellmotive/ApexPost/pull/15) | Close the remaining code gaps from re-checking the brief | MERGED |

## Commits on `master` (merges left out)

| Commit | Date | Message |
|---|---|---|
| `07f39c7` | 2026-10-23 | Add .gitattributes, .gitignore, and README.md. |
| `4c5b571` | 2026-10-23 | Add project files. |
| `8574cdc` | 2026-10-23 | feat(domain): core domain models, business rules1-6, and xunit tests |
| `3065b8e` | 2026-10-25 | Final submittion commit. |
| `3d55179` | 2026-10-31 | fix: address Week 2 assessment remarks |
| `bc2f5dc` | 2026-10-31 | fix: close remaining Week 2 assessment gaps |
| `645175c` | 2026-10-31 | feat: move persistence to SQL Server with EF Core 10 |
| `5481b0e` | 2026-10-31 | feat: capture SQL Server evidence and replace low-stock index |
| `9b88289` | 2026-10-31 | refactor: replace ViewBag in staff and service forms |
| `e9f9868` | 2026-10-31 | feat: store shipment destination address as an owned type |
| `c2438ed` | 2026-10-31 | feat: retry sales that lose a rowversion race |
| `17164fa` | 2026-10-31 | fix: stop logging expected failures as EF Core errors |
| `ed96297` | 2026-10-31 | test: prove the least-privilege role on SQL Server |
| `3f45ba1` | 2026-10-31 | docs: describe the SQL Server system and refresh evidence |
| `b8b37b1` | 2026-10-31 | refactor: move line item and manager rules into the Domain |
| `9d9d171` | 2026-10-31 | test: force a real deadlock on SQL Server |
| `568e21e` | 2026-10-31 | test: add a load test script and its results |
| `7f900f9` | 2026-10-31 | docs: record load and deadlock evidence and Git history |
| `96f84eb` | 2026-10-31 | fix: restock voids and refunds through the procedure in one transaction |
| `92af4e2` | 2026-10-31 | docs: add ER columns, raw statistics output and consistent test counts |
| `ea1106d` | 2026-10-31 | docs: explain the W2 estimate mismatch with measured evidence |
| `4559425` | 2026-10-31 | style: apply dotnet format whitespace fixes |
| `03b908c` | 2026-10-31 | test: cover the high-value money order boundary at checkout |
| `44ddec5` | 2026-10-31 | docs: bring the Git history snapshot up to PR #8 |
| `62bd687` | 2026-10-31 | fix: record the real user on stock history and test reports on SQL Server |
| `030dc08` | 2026-10-31 | fix: make HAVING do real work in the reporting queries |
| `35d2e5a` | 2026-10-31 | test: restore the Week 2 rules file and name tests by behaviour |
| `dbfe414` | 2026-10-31 | docs: tidy ADRs, record deviations and update counts |
| `bdf981c` | 2026-10-31 | fix: return Problem Details from void, refund, stock and admin errors |
| `bafa304` | 2026-10-31 | docs: record the error boundary decision and update counts |
| `c439882` | 2026-10-31 | docs: make the README setup work from a clean unzip |
| `96d1c28` | 2026-10-31 | refactor: read basket stock in one query and move rules into the Domain |
| `a137b4f` | 2026-10-31 | docs: reproduce the W2 estimate and measure procedure against LINQ |
| `bc8fe0d` | 2026-10-31 | docs: strengthen ADR-27, add ADR-39 and update counts |
| `7ed05d4` | 2026-11-01 | fix: reject negative shipment sizes and unknown staff on stock changes |
| `be638e4` | 2026-11-01 | fix: return Problem Details from every error response |
| `22a9990` | 2026-11-01 | refactor: make QueryShapes internal |
| `36164a6` | 2026-11-01 | docs: record the staff email and Week 2 test deviations and update counts |
| `44443d8` | 2026-11-01 | build: prune the vulnerable Pkcs package instead of referencing a patched one |
| `1af8f10` | 2026-11-01 | fix: answer 400 when a form is shown again, and reject a price change that rounds to zero |
| `61d4a84` | 2026-11-01 | feat: require positive payments, unicode role names, and drop a redundant index |
| `8da478b` | 2026-11-01 | docs: measure the stock history index |

## Week 4: branches and pull requests

The four parts are chained: each branch starts from the one before it, so merge them in order, A, B, C, D. All of them are merged into `master`: #17 to #20 in order, A to D; then #21, #22 and #23, which hold the review fixes and the later documentation fixes (some commits were pushed after an earlier PR had merged, so they went into the next one). The pull request page is the record of when each merged.

| PR | Branch | Part |
|---|---|---|
| [#17](https://github.com/vercellmotive/ApexPost-TusharSharma/pull/17) | `week4-partA` | Part A: the courier offline store |
| [#18](https://github.com/vercellmotive/ApexPost-TusharSharma/pull/18) | `week4-partB` | Part B: the shipment REST API |
| [#19](https://github.com/vercellmotive/ApexPost-TusharSharma/pull/19) | `week4-partC` | Part C: OpenAPI, versioning and the client SDK |
| [#20](https://github.com/vercellmotive/ApexPost-TusharSharma/pull/20) | `week4-partD` | Part D: security and the courier app |
| [#21](https://github.com/vercellmotive/ApexPost-TusharSharma/pull/21) | `week4-review-fixes` | Review fixes: security hardening, sync bugs, refreshed evidence |
| [#22](https://github.com/vercellmotive/ApexPost-TusharSharma/pull/22) | `week4-final-review-fixes` | Dev-token guards, 429 declared, AllowedHosts, API-NOTES and README fixes |
| [#23](https://github.com/vercellmotive/ApexPost-TusharSharma/pull/23) | `week4-review-docs-and-capture` | Wording fixes and the evidence capture scripts |

### Part A: the courier offline store (`week4-partA`)

| Commit | Date | Message |
|---|---|---|
| `aa772af` | 2026-11-02 | feat(courier): add the offline library with the scan model, database, indexes and change listener |
| `8a7c350` | 2026-11-02 | feat(courier): add the scan repository with key-value CRUD and parameterised SQL++ |
| `6d4e93d` | 2026-11-02 | feat(courier): add the outbox service and a fake uploader |
| `e6585ff` | 2026-11-02 | feat(courier): add the console test harness |
| `c5f0ee9` | 2026-11-02 | test(courier): add tests for the offline library and the harness |
| `07af09c` | 2026-11-02 | docs(courier): add the design note, README section and evidence |

### Part B: the shipment REST API (`week4-partB`)

| Commit | Date | Message |
|---|---|---|
| `c6262e3` | 2026-11-09 | feat(tracking): add the shipment and scan domain and the shipment service |
| `39fe7d0` | 2026-11-09 | feat(tracking): add the tracking schema, migration and EF Core store |
| `510677a` | 2026-11-09 | feat(api): add the shipment REST API |
| `8d248d7` | 2026-11-09 | test(api): cover the rules, the service, every status code and SQL Server |
| `de44b98` | 2026-11-09 | docs(api): add API-NOTES.md, the README section and the request file notes |

### Part C: OpenAPI, versioning and the client SDK (`week4-partC`)

| Commit | Date | Message |
|---|---|---|
| `1f5799e` | 2026-11-09 | feat(api): add URL versioning, per-version OpenAPI documents and Swagger UI |
| `89a39f0` | 2026-11-09 | feat(client): generate the v2 client with NSwag and add a console consumer |
| `efb5e57` | 2026-11-09 | docs(api): add VERSIONING.md, the OpenAPI explanation and Part C evidence |

### Part D: security and the courier app (`week4-partD`)

| Commit | Date | Message |
|---|---|---|
| `c548a98` | 2026-11-09 | feat(api): secure the API with JWT bearer tokens, roles and partner API keys |
| `5ffd5d9` | 2026-11-09 | feat(offline): keep an optional recipient signature on a scan |
| `448787e` | 2026-11-09 | feat(courier): add the typed API client, resilience, JSON, sync and view models |
| `5860fe3` | 2026-11-09 | feat(courier): add the .NET MAUI app with Login, New Scan, Pending and Sync Status |
| `7dfc3f6` | 2026-11-09 | docs: add SECURITY.md, RESILIENCE.md, the Part D README section and evidence |

## Early commit messages

The first commits have weak messages, such as `3065b8e` ("Final submittion commit."), which holds 84 files. They stay as they are: rewriting them would change every later commit hash on `master`, and break the links from the pull requests above. Work since then uses a feature branch, a pull request and a message that says what changed.
