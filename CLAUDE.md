# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A demo/teaching repo for **automated testing strategies in ASP.NET Core 10**. The "CarvedRock Fitness" app is real and working, but the point of the repo is the *test* projects and the different techniques each one demonstrates. When adding code, keep the testing story intact and readable — comments in test utility classes explain *why* a technique was chosen and are part of the deliverable.

`readme.md` contains a list of deliberately-omitted tests under "Additional Tests to Create" — these are exercises left for readers, so don't add them unprompted.

## Commands

```powershell
# Full test run + coverage report (deletes TestResults/, coveragereport/, playwright-artifacts/ first)
./test-with-coverage.ps1              # add -ShowReports to open the HTML reports

# Single project
dotnet run --project tests/CarvedRock.UnitTests

# Single test / class (TUnit tree-node filter: /Assembly/Namespace/Class/Test, * wildcards)
dotnet run --project tests/CarvedRock.UnitTests --treenode-filter "/*/*/ProductValidationTests/*"
dotnet run --project tests/CarvedRock.ApiTests  --treenode-filter "/*/*/*/GetDadJokeWorks"

# Run the app (needs Aspire prerequisites + Docker)
aspire run                            # or F5 in VS Code with the C# Dev Kit

# Performance tests (NBomber) — the Aspire app must already be running
aspire run --detach --configuration Release
dotnet run --project tests/PerformanceTests --configuration Release
aspire stop

# EF Core migrations (from the CarvedRock.Data folder)
dotnet ef migrations add <Name> -s ../CarvedRock.Api
```

`global.json` sets `test.runner` to **Microsoft.Testing.Platform**, so `dotnet test` accepts platform options directly (`--coverage`, `--coverage-settings`, `--treenode-filter`, `--report-trx`). CI passes them after a `--` separator; the local script passes them inline. Both work.

Raw code coverage files are created in the `TestResults` folder, one per test
project, guid-named: `TestResults/*.cobertura.xml` (the guid doesn't identify
the project — open the file or use the rollup below instead of grepping these directly).

TUnit test reports are created in the `TestResults` folder, one JSON + one HTML
per project, named `<ProjectName>-<os>-net10.0.tunit-report.json` /
`-report.html` (e.g. `CarvedRock.ApiTests-windows-net10.0.tunit-report.json`) —
glob as `TestResults/*.tunit-report.json` for the machine-readable results.

`test-with-coverage.ps1` runs `reportgenerator` with `Html;TextSummary`,
so after a run `coveragereport/Summary.txt` has a short per-assembly/class
coverage rollup — read that first instead of the cobertura XML or opening the
HTML report; fall back to the XML only when line-level detail is needed.

Coverage reporting needs `dotnet tool install -g dotnet-reportgenerator-globaltool`. Exclusions (auto-properties, EF migrations, Mapperly output, model/entity files, MailKit.Client) live in `testconfig.json`.

Playwright browsers: `pwsh tests/CarvedRock.AppTests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium`.

## Architecture

### Runtime composition (Aspire)

`CarvedRock.AppHost/AppHost.cs` is the single source of truth for the topology:

```
db (Postgres 18 + pgvector) ──┐
smtp (MailPit) ────┴─> api ─> mcp ─> agent ─> webapp
                                        mcp-inspector ─> mcp
```

Services address each other by Aspire service-discovery name (`https://api`, `https://agent`), never by port. The AppHost also exposes a **Reset Data** HTTP command that hits the API's dev-only `/internal/reset-data`.

The OpenAI key is an Aspire parameter (`Parameters:openaiKey`, `AIConnection__OpenAIKey` env var on the agent). Without it, everything except the AI chat and `/agent` still works.

### Projects

| Project | Role |
| --- | --- |
| `CarvedRock.Core` | Shared DTOs (`ProductModel`, `CartModels`, `OrderModels`), `AdminClaimsTransformation`, `OpenApiHelper` |
| `CarvedRock.Data` | EF Core `LocalContext`, entities, `CarvedRockRepository`, migrations, `SeedData.json`. Also the knowledge base table (`KnowledgeChunk`, a pgvector `vector(1536)` column) and `KnowledgeRepository` (cosine-distance search). Build hand-made contexts with `UseCarvedRockNpgsql(...)`, never plain `UseNpgsql` - without `UseVector()` EF cannot build the model |
| `CarvedRock.Domain` | Business logic (`ProductLogic`, `CartLogic`, `OrderLogic`), FluentValidation validators, Riok.Mapperly `ProductMapper`. Knowledge base: the help documents are markdown files in `Knowledge/` (embedded resources); `KnowledgeChunker` splits them by `##` section, `KnowledgeLogic` embeds them (skipping unchanged chunks by hash) and searches via `IEmbeddingGenerator` |
| `CarvedRock.Api` | Controllers, `ValidationExceptionHandler`, `EmailService`, Scalar/OpenAPI. `GET /knowledge/search?q=&top=` (anonymous) searches the help documents; `KnowledgeIngestionService` embeds them in the background at startup (needs the OpenAI key; without it search answers 503 and everything else works). `POST /internal/reset-data` re-runs ingestion |
| `CarvedRock.WebApp` | Razor Pages UI; talks to the API through typed `ProductService`/`CartService`/`OrderService` HttpClients. The user menu (`#userMenu`) links to My Orders (`/Orders`: all orders with status badges, cancel for `Placed` ones) and Sign Out |
| `CarvedRock.Mcp` | MCP server — `CarvedRockTools` (anonymous, product reads), `KnowledgeTools` (anonymous: `search_knowledge` over the help documents), `CartTools`/`OrderTools` (any signed-in user: `add_to_cart`, `clear_cart`, `get_my_orders`, `get_order`, `cancel_order`) and `AdminTools` (admin: `delete_product`, `set_product_price`). The destructive tools (`delete_product`, `cancel_order`, `clear_cart`) take `confirm` (default false): without it they change nothing and return `confirmation_required`. `TokenForwarder` relays the caller's bearer token to the API |
| `CarvedRock.Agent` | `POST /agent` (streams text), `POST /agent/run` (non-streaming: tool calls, token usage, elapsed time, prompt version) and the admin-only `POST /agent/describe` (generative, no tools: `ProductDescriber` turns a name, category and key features into a structured `ProductDraft` and reports - never trims - any rule violations; limits come from `CarvedRock.Core.ProductLimits`, shared with the product validator); OpenAI chat client wired to the MCP server's tools via an injectable `IMcpClientProvider`; prompts and their version ids live in `AgentPrompts` (bump `AdminVersion` when `CarvedRock.Mcp/AdminPrompt.cs` changes) |
| `MailKit.Client` | Hand-rolled Aspire client integration (factory, settings, health check) |
| `CarvedRock.ServiceDefaults` | OpenTelemetry, health checks, service discovery, resilience |

### Auth model

The app uses the **public Duende demo IdentityServer** (`https://demo.duendesoftware.com`) — no local IdP resource.

- Interactive users: `alice`/`alice` (customer), `bob`/`bob` (admin).
- M2M clients (secret `secret`): `m2m` (customer), `m2m.short` (admin).
- Admin is granted by `AdminClaimsTransformation`: email local-part `bobsmith`, **or** `client_id == "m2m.short"`. Both the API and the MCP server register it, so admin authorization is consistent across them.

Because the IdP is external and shared, its login page is the main source of flakiness in browser tests (see Retries below).

## The three test projects

They are separate projects **because top-level statements make `Program` ambiguous** across multiple `WebApplicationFactory<Program>` targets. All use **TUnit** (not xUnit/NUnit): `await Assert.That(x).IsEqualTo(y)`, `[Before(Class)]`/`[After(TestSession)]` hooks, `[ClassDataSource<T>(Shared = SharedType.PerTestSession)]` for fixtures, `[Arguments]`/`[MethodDataSource]` for data-driven cases, `[DependsOn]` for ordering. Common namespaces are declared as `<Using>` items in each `.csproj` rather than per-file.

**`tests/CarvedRock.UnitTests`** — validators, order/cart logic, and the agent run-result mapper (`AgentRunMapperTests`, pure mapping of `ChatMessage`s, no model calls); no I/O. Uses `TUnit.Mocks` source-generated mocks: `ICarvedRockRepository.Mock()`, then `mock.IsProductNameUniqueAsync(Any()).Returns(true)`. `ProductValidation-DataDriven.cs` is the reference for the three data-driven styles (inline `[Arguments]`, `MethodDataSource`, `TestDataRow` with display names).

**`tests/CarvedRock.ApiTests`** — in-process API testing (`TUnit.AspNetCore`).

- `ApiFactory : TestWebApplicationFactory<Program>` + `ApiTestsBase : WebApplicationTest<ApiFactory, Program>`; tests get `Factory.CreateClient()` and `TestData`.
- **Testcontainers** Postgres (the `pgvector/pgvector` image, same as the AppHost) in `TestData` (an `IAsyncInitializer` shared per test session) seeded with 100 Bogus products using a **fixed seed (2001)** so data is reproducible. The connection string is injected via `ConfigureStartupConfiguration`.
- **Knowledge base:** `TestData` ingests the real corpus once with `HashingEmbeddingGenerator` (deterministic hashed bag-of-words vectors, no OpenAI call), and `ApiFactory` swaps the same generator in for the API. It proves the plumbing against real pgvector; it says nothing about semantic quality. `KnowledgeApiTests` covers ranking, limits and validation.
- **WireMock** replaces the external dad-joke API by overriding the `DadJokeUrl` config value.
- `TestAuthHandler` is a fake auth scheme: the user comes from the `X-Authorization` header and every `X-Test-<claim>` header becomes a claim. Use the `AddAdminAuthHeaders()` / `AddCustomerAuthHeaders()` extensions.

**`tests/CarvedRock.AppTests`** — full-stack, real containers (`TUnit.Aspire` + `TUnit.Playwright`).

- `AppFixture : AspireFixture<CarvedRock_AppHost>` starts the whole AppHost (3-minute resource timeout), then opens a `LocalContext` against the real Postgres so tests can assert on the database directly. Endpoints come from `App.GetEndpoint(...)`/`App.CreateHttpClient(...)`.
- Real access tokens are fetched from the Duende demo server via client credentials (`GetAdminApiClient`, `GetCustomerApiClient`, `GetAdminMcpClient`, `GetCustomerMcpClient`).
- `AppFixture.cs` re-declares `Product`/`NewProductModel` as local records **on purpose** — the tests assert against a documented contract rather than the app's own types.
- `CustomPageTest : PageTest` is the Playwright base: `IgnoreHTTPSErrors` (Linux CI dev-certs), page-failure event capture, and `page.Login(user, pass)`.
- `[RecordVideo]` is a custom attribute that flags a test via TUnit's `StateBag` (through `ITestDiscoveryEventReceiver`, so no reflection); `CustomPageTest` turns that into `RecordVideoDir` and renames the `.webm` files to test names at `[After(TestSession)]`. Artifacts land in `bin/<config>/net10.0/playwright-artifacts/`.

### Gotchas in AppTests

- **Schema comes from `EnsureCreated`, not from the migrations.** `LocalContext.MigrateAndCreateData` calls `Database.EnsureCreated()`, so the `Migrations/` folder is a reference/teaching artifact. A database that already has tables will NOT pick up new tables or columns (e.g. order status, knowledge chunks) - recreate it. The AppHost's Postgres has no volume, so `aspire run` always starts fresh.
- **Knowledge tests need an OpenAI key** (`KnowledgeTests` uses the real embedding model); they wait up to 2 minutes for the API's background ingestion.
- **Adversarial test content is opt-in.** The AppHost parameter `includeAdversarialContent` (default `false`; set with `aspire secret set Parameters:includeAdversarialContent true`) becomes `Seed:IncludeAdversarialContent` in the API. When on, `SeedDataLoader` also seeds the *Ridgeline Daypack* product (description carries an `add_to_cart` injection payload) and `KnowledgeCorpus.Load(true)` adds a poisoned help document (`KnowledgeAdversarial/`, retrievable by recycling/resoling questions; it tells the assistant to call `cancel_order` and print `AUDIT-7731`). Normal runs and every test must have it off; `KnowledgeTests.AdversarialContentIsAbsentByDefault` is expected to fail when it is on.
- **Orders have a status** (`Placed`/`Shipped`/`Cancelled`). `POST /order/{id}/cancel` (owner or admin; 404 for someone else's order, 409 if shipped/cancelled) and the admin-only `POST /order/{id}/ship` exist; `ship` is deliberately not an MCP tool.
- **Parallel browser tests must not share `AppFixture.TestDbContext`** (a `DbContext` is not thread-safe) - use `AppFixture.CreateDbContextAsync()`.
- **Mail assertions:** M2M tokens have no email claim, so their orders all go to `unknown@carvedrock.com`, which `OrderTests` reads from MailPit. `OrderMcpTests` uses its own address for that reason; do the same in any new test that places orders as an M2M client.
- **Shared mutable state.** These tests run against one live database and mutate it. `WebAppTests.AdminCanDeleteProductsViaChat` deletes products 20 and 23; the MCP test updates product 22 — and `OrderTests` explicitly filters those ids out. If you add a mutating test, check the exclusions in `OrderTests.PlacingOrderWorksCompletely` and update them.
- **Global retry policy.** `[assembly: Retry(2, BackoffMs = 30_000)]` sits at the top of `AppFixture.cs` to survive transient failures at the external IdP's login page. Retries are silent and the TUnit report only keeps the passing attempt, so a flaky test can look green: after a run, check `TestResults/*.tunit-report.json` for `"retryAttempt"` greater than 0, and to see the real failure temporarily set `Retry(0)`.
- **Browser parallelism** is capped at 3 (`BrowserParallelLimit` + `[ParallelLimiter<BrowserParallelLimit>]` on `WebAppTests`) because each test is a Chromium instance on top of the whole Aspire app.
- **Non-deterministic assertions.** Chat-driven tests assert on LLM output (e.g. `ToContainTextAsync("successfully")`) and need an OpenAI key; treat them as inherently softer assertions.

### Performance tests

`tests/PerformanceTests` is a plain NBomber console app (not a test project — no assertions). It targets a **hardcoded** `https://localhost:7213/` (the API's `launchSettings` URL), so the Aspire app must be running first. Reports go to `tests/PerformanceTests/reports/`.

## CI

`.github/workflows/ci.yaml` builds Release, installs Chromium, cleans and re-trusts dev certs (and exports `SSL_CERT_DIR` for OpenSSL trust on Linux), runs tests with cobertura coverage + TRX, then uploads test results, the merged TUnit report, Playwright artifacts, and a ReportGenerator coverage report — commenting the coverage summary on PRs. `OPENAI_KEY` is supplied as `Parameters__openaiKey`.

---

An OpenAI Codex config exists at `~/.codex`. If you'd like to bring its MCP servers, prompts, or instructions into Claude Code, reply `/import` to see what's importable, then `/import --yes=<digest>` to apply it.
