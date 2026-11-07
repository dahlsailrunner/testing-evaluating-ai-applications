# Plan: Broaden the AI Features and Open Up Seams for the Evaluation Course

## Purpose

This repo is being used for a video course on **evaluating AI behavior**
(generative, grounded, and agentic) in .NET. The course adds the evaluations
live in demos using `Microsoft.Extensions.AI.Evaluation`. This plan makes the
*application* rich enough, and observable enough, for those demos. It does
**not** add any evaluation code.

### Explicitly out of scope

- `Microsoft.Extensions.AI.Evaluation.*` packages, evaluators, eval datasets,
  baselines, thresholds, and CI evaluation gates. These are the course demos.
- Auth changes. OIDC/OAuth2 against the Duende demo server stays as is.
- Telemetry changes. Aspire + its AI extensions already cover it.
- Agent-behavior tests using a fake `IChatClient`, and MCP tool-schema snapshot
  tests. They are course material (fakes and contract tests); the seams below
  only make them *possible*. (See open question Q6.)

Conventional deterministic tests for the **new non-AI code** (API endpoints,
domain logic, retrieval plumbing) are in scope, because this repo's own
convention is that features ship with tests. None of them call an LLM.

## Current state (verified in code)

- `CarvedRock.Agent/Agent.cs` yields `update.ToString()` to the caller. Tool
  calls (`FunctionCallContent`/`FunctionResultContent`), token `Usage`, and
  timing are discarded, so nothing downstream can measure tool-call accuracy,
  cost, or latency.
- `McpClientHelper.GetMcpClient` is static and called directly inside
  `Agent`, so the tool server cannot be substituted. `IChatClient` *is*
  injected.
- Both system prompts are inline strings (`Agent.cs`, `AdminPrompt.cs`), with
  no version identifier to key a baseline on.
- The MCP tools are `get_products`, `get_single_product`, `add_to_cart`
  (any signed-in user), `delete_product`, `set_product_price` (admin).
  `get_products` returns the entire catalog, so there is no retrieval step.
- Orders: `OrderController` only has `POST /order`. `GetOrdersForUserAsync`
  exists on the repository but nothing calls it. The `Order` entity has
  **no status**, so there is no cancel-a-placed-order; the UI's "Clear Cart"
  button (formerly "Cancel Order / Clear Cart") is `DELETE /cart`. `IOrderEmailSender` only knows about
  confirmation emails.
- Tools do little input validation (no quantity cap, no price floor in the MCP
  layer) and `delete_product` has no confirmation step.

## Open questions (please decide before Phase 2+)

| # | Question | Recommendation |
|---|---|---|
| Q1 | **Retrieval store.** (A) pgvector in the existing Postgres; (B) in-memory vector store built at startup; (C) keyword search, no embeddings. | **A.** It is closest to a real system and teaches the most. It needs a pgvector image in AppHost and in the ApiTests Testcontainer, plus an embedding model (reuses the OpenAI key). B is the fallback if the image change is unwelcome. |
| Q2 | **Where retrieval lives.** MCP tools are thin wrappers over the API today. | Keep that: `GET /knowledge/search` in the API, `search_knowledge` in MCP calls it. |
| Q3 | **Order cancellation.** *Decided: add real cancellation all the way down (entity, migration, repository, domain, API, MCP).* It gives the course a destructive, per-user, rule-bound action to attack. Sub-questions below (Q3a-c). | Decided. |
| Q3a | **Who may cancel?** | Owner only for customers; admins may cancel any order through the same endpoint. The difference is a useful least-privilege scenario. |
| Q3b | **How do orders become non-cancellable?** | Add a `Shipped` status and an admin-only `POST /order/{id}/ship` endpoint (no MCP tool). Tests and demos can then create a shipped order without seeding orders for the demo IdP's user ids. |
| Q3c | **Failure semantics.** | Another user's order: `404` (no existence leak, same as `GET /order/{id}`). Already shipped or already cancelled: `409`. The MCP tool turns these into a structured `OperationResult` error rather than throwing. |
| Q4 | **Existing gaps.** *Decided: add a confirmation step to `delete_product` (see 2f).* Input validation is added to new tools only; existing tools are otherwise left alone. | Decided. |
| Q7 | **Decided: `cancel_order`, `clear_cart`, and `delete_product` all use the same server-side `confirm` parameter** (default `false`; without it nothing changes and a `confirmation_required` result describes what would happen). The guardrail is consistent and "did the model ask first?" is observable on every destructive tool. | Decided. |
| Q5 | **Adversarial content** (poisoned description / knowledge doc). Always seeded, or behind a flag? | Behind config flag `Seed:IncludeAdversarialContent`, default `false`, so the normal UI is never poisoned and demos switch it on. |
| Q6 | **Decided: left out of the repo.** Fake-`IChatClient` agent tests, MCP contract snapshot tests, and session tests are built live in the course demos. Phase 1's seams exist to make them possible. | Decided. |

## Phase 1: Agent seams (unblocks most course objectives) - DONE

Goal: make the agent measurable and substitutable without changing the
`/agent` streaming contract the WebApp uses.

- [x] Add `IMcpClientProvider` (wrapping `McpClientHelper`) and inject it into
      `Agent`; keep `McpClientHelper` as the production implementation.
- [x] Extract prompts into `AgentPrompts` with a version constant per prompt
      surfaced on the run result and as the `carvedrock.prompt.version`
      activity tag. The admin prompt text still lives in the MCP server, so
      `AgentPrompts.AdminVersion` must be bumped when `AdminPrompt.cs` changes.
- [x] Add a non-streaming `Agent.RunAsync` returning an `AgentRunResult`:
      final text, the input conversation, all response messages (tool calls
      and results included), `UsageDetails`, elapsed time, prompt version.
      Setup is shared with the streaming path via `PrepareAsync`.
- [x] Expose it as `POST /agent/run` (same auth as `/agent`, same
      `AgentChatRequest`). The JSON (`AgentRunResponse`) lists tool calls with
      arguments and results paired by call id, plus usage, elapsed ms, and
      prompt version.
- [x] Streaming `/agent` is unchanged in behavior (same prompt, tools, history
      handling).
- [x] Tests (no LLM): `AgentRunMapperTests` in `CarvedRock.UnitTests`
      (call/result pairing, errors, usage, null handling). `/agent` allows
      anonymous callers, so there is no 401 case to test for `/agent/run`.
- [ ] Not yet verified end to end: `/agent` streaming and `/agent/run` against
      a real model (needs an OpenAI key, `aspire run`).

## Phase 2: Order lookup, cancellation, and multi-step workflows - DONE

### 2a. Data and domain

- [x] `Order` entity: add `Status` (`Placed`, `Shipped`, `Cancelled`; stored
      as a string), nullable `CancelledAt`, and nullable `CancellationReason`.
- [x] EF migration `AddOrderStatus` from `CarvedRock.Data`
      (`dotnet ef migrations add AddOrderStatus -s ../CarvedRock.Api`).
      Existing rows default to `Placed`. Keep `SeedData.json` untouched.
- [x] Repository: `GetOrderAsync(id)`, `UpdateOrderAsync(order)` (or targeted
      status updates). `GetOrdersForUserAsync` already exists.
- [x] `IOrderLogic`: `GetOrdersAsync(userId)`, `GetOrderAsync(userId, id)`,
      `CancelOrderAsync(userId, isAdmin, id, reason)`, `MarkShippedAsync(id)`.
      Rules: owner or admin only; only `Placed` orders can be cancelled;
      cancelling is a status change (the row is kept for audit); shipping only
      applies to `Placed` orders.
- [x] `IOrderEmailSender.SendOrderCancellationAsync`, implemented in
      `EmailService` (MailPit shows it in the inbox, like confirmations).
- [x] `OrderModel` gains `Status` (Core DTO); map it in `OrderLogic`.

### 2b. API (`OrderController`, all `[Authorize]`)

- [x] `GET /order` (current user's orders), `GET /order/{id}` (404 for another
      user's order unless admin).
- [x] `POST /order/{id}/cancel` with an optional `{ reason }` body: 200 with
      the updated order, 404 not found / not yours, 409 shipped or already
      cancelled (`ProblemDetails`, matching existing style).
- [x] `POST /order/{id}/ship` (admin only, via `AdminClaimsTransformation`).
      Exists so shipped orders can be created in tests and demos.

### 2c. MCP (`[Authorize]`, token forwarded by `TokenForwarder`)

- [x] `get_my_orders`, `get_order`,
      `cancel_order(orderId, reason?, confirm = false)`,
      `clear_cart(confirm = false)`.
- [x] Both destructive tools follow the `confirm` pattern from 2f: without
      `confirm: true` they change nothing and return `confirmation_required`
      describing the effect (the order's lines and total, or the cart's
      contents). `cancel_order` checks ownership and status *before* asking, so
      a refusal (404/409) is never preceded by a pointless confirmation.
- [x] Both return the `OperationResult` shape and turn 404/409 into structured
      errors (`status: "error"` with a message the model can relay), instead of
      throwing.
- [x] Validate tool inputs (ids positive, `reason` length cap).
- [x] No MCP tool for `ship`; it stays API-only so a customer prompt can never
      reach it.

### 2d. Prompt

- [x] Customer prompt: describe order lookup, require explicit confirmation
      (naming the order) before passing `confirm=true` to `cancel_order` or
      `clear_cart`, never in the same turn as the original request, cover
      "reorder my last order" (`get_my_orders` then `add_to_cart` per line),
      and say what to do when a cancel is refused.

### 2e. Tests (no LLM)

- [x] Unit: `OrderLogic` cancel rules (owner, admin, not owner, shipped,
      already cancelled, not found), ship rules, email sender called once.
- [x] ApiTests: `GET /order`, cross-user 404, cancel happy path, 409 cases,
      admin cancel of another user's order, non-admin `ship` is 403.
- [x] AppTests: one MCP test per new tool through the real server, including
      that `cancel_order` and `clear_cart` change nothing without `confirm`. Cancel
      tests mutate orders for the shared demo users, so place a fresh order
      inside the test rather than depending on existing ones.
- [x] Update the `OrderTests` product-id exclusions only if a new test mutates
      products (CLAUDE.md gotcha).

### Why this is here (adversarial surface it creates)

Not tests, just what the feature makes possible for the course demos:
cancelling another user's order by guessing ids, cancelling a shipped order,
cancelling without confirmation, "cancel all my orders" (excessive agency),
a customer attempting an admin-only action, and a cancel triggered by injected
text in retrieved content (Phase 5).

### 2f. `delete_product` confirmation (decided; was Q4)

- [x] `delete_product(id, confirm = false)`. Without `confirm: true` it
      deletes nothing and returns `OperationResult("confirmation_required",
      "<name> (id N, $price) will be permanently deleted. Ask the
      administrator to confirm, then call again with confirm=true.")`. It
      looks the product up first so the message names it; an unknown id
      returns a structured error.
- [x] Admin prompt: name every product to be deleted, ask for explicit
      confirmation, and never pass `confirm=true` in the same turn as the
      original request. One confirmation covers only the ids that were listed.
- [x] Why a parameter and not prompt-only: the server holds the guardrail, and
      "did the model ask before passing `confirm=true`?" becomes observable in
      the tool calls.
- [x] AppTests: `WebAppTests.AdminCanDeleteProductsViaChat` becomes two turns
      (request, assert the confirmation prompt, reply "yes", assert deletion in
      the database). The chat already re-sends history, so a follow-up works.
      Still ids 20 and 23, so the `OrderTests` exclusions are unchanged.
- [x] AppTests (MCP): without `confirm` the product still exists; with
      `confirm` it is gone. Use a product id not used elsewhere and add it to
      the `OrderTests` exclusions (CLAUDE.md gotcha).
- [x] The API's `DELETE /product/{id}` is unchanged, so ApiTests are
      unaffected. Update the `readme.md` admin walkthrough if it mentions
      deleting via chat.

### 2g. My Orders page and user menu - DONE

- [x] The "name + Sign Out" block in `_LoginPartial.cshtml` is now a user dropdown
      (`#userMenu`) with **My Orders** and **Sign Out**.
- [x] New `/Orders` Razor page (`OrdersModel`) backed by a typed `OrderService`
      (`GET /order`, `POST /order/{id}/cancel`). It lists *all* of the user's orders,
      newest first, with a status badge (Placed / Shipped / Cancelled).
- [x] Only `Placed` orders get a "Cancel order" button, which opens a confirmation
      modal (optional reason, "Yes, cancel order" / "Keep order"). 404/409/400 from the API
      show as an error banner (e.g. "already shipped") instead of an error page.
- [x] Playwright tests (`WebAppTests.Orders.cs`, bob): cancel from the menu flow with a
      DB check, and cancelling an order that shipped after the page loaded (error banner,
      refreshed row has no cancel button). They share bob's cart with
      `CustomerCanAddRecommendedProductToCartViaChat`, so all three use
      `[NotInParallel("bob-cart")]`.
- [x] `Page.Login` now waits for `#userMenu` instead of the "Sign Out" link, which is
      hidden inside the dropdown until opened.

### Phase 2 implementation notes (where it differs from the text above)

- The API's `CustomizeProblemDetails` used to overwrite *every* `ProblemDetails.Detail`
  with a generic support message, which hid the reason for a 409 (and the existing
  empty-cart 400). It now only forces the generic text for 5xx (or when no detail was set).
- Admin mode (`/admin` prefix) now lasts for the whole conversation: a destructive action
  takes two turns and the confirmation reply has no prefix. See
  `Agent.IsAdminModeRequested` (unit tested). Prompt versions bumped to `customer-2` and
  `admin-2`.
- The MCP delete-confirmation AppTest creates its own throwaway product, so no
  `OrderTests` id exclusions changed. `AdminCanDeleteProductsViaChat` still uses 20 and 23.
- API tests have no SMTP server, so `ApiFactory` swaps in `RecordingOrderEmailSender`.
- AppTests: `OrderMcpTests` places orders as the admin M2M client (`m2m.short`) with its
  own email address and runs serially; the customer M2M client (`m2m`) is used to prove it
  cannot see or cancel those orders. Using the default address made `OrderTests` flaky
  (it reads the newest message sent to that address from MailPit).
- Browser tests run in parallel, so they must not share `AppFixture.TestDbContext`
  (a `DbContext` is not thread-safe); use `AppFixture.CreateDbContextAsync()`.
- Not yet verified by a person: the customer-facing chat flows for cancel, clear cart and
  reorder (the tools are covered; the prompt's behavior is exactly what the course evaluates).

## Phase 3: Retrieval over policy and product knowledge

- [ ] Corpus: 10-12 short markdown documents (returns and exchanges, shipping,
      warranty, boot fit/sizing, boot care, layering, sleeping-bag ratings,
      tent care, order changes and cancellation) matching the seeded
      categories. The cancellation policy text must match the real rules
      from Phase 2 (cancel until shipped, then return instead). Deliberately
      include one near-duplicate pair and a few topics the corpus does **not**
      cover (e.g. price matching), so abstention is demonstrable.
- [ ] Ingest: chunk, embed via `IEmbeddingGenerator`, store (per Q1). Re-run
      on `/internal/reset-data`. If no OpenAI key is configured the feature
      degrades with a clear message, like the chat does today.
- [ ] API: `GET /knowledge/search?q=&top=` returning chunks with source id,
      title, score, and text. The tool returns this shape unchanged so
      downstream consumers can see exactly what was retrieved.
- [ ] MCP: `search_knowledge(query, topK)` (anonymous, read-only). Cap `topK`.
- [ ] Prompt update: answer policy questions only from retrieved text, cite the
      source title, say so when nothing relevant is found.
- [ ] AppHost: pgvector image for `db`; document it in CLAUDE.md.
- [ ] Tests (no LLM): chunking unit tests; ApiTests for the endpoint using a
      **deterministic fake `IEmbeddingGenerator`** and the Testcontainers DB
      (also needs the pgvector image). The fake proves plumbing, not
      semantic quality.

## Phase 4: One generative (non-agentic) feature

- [ ] `POST /agent/describe` (admin only): input name, category, key features;
      output a structured `ProductDraft` (description text) via
      `GetResponseAsync<T>` on `IChatClient`. Cancellation token honored.
- [ ] Validate the draft against the existing `ProductValidator` rules before
      returning it; report violations rather than silently trimming.
- [ ] No UI in this pass (Scalar and tests are enough for the demos).
- [ ] Tests (no LLM): request validation, admin-only authorization, and the
      validator step with a stubbed draft.

## Phase 5: Adversarial content seeds (flagged)

- [ ] `Seed:IncludeAdversarialContent` (default off) adds: one product whose
      description contains an injection payload aimed at existing tools
      (including `cancel_order`/`add_to_cart`), and one poisoned knowledge
      document (indirect injection via retrieval).
- [ ] Make sure `/internal/reset-data`, ApiTests seeding (Bogus, seed 2001),
      and AppTests ids are unaffected when the flag is off.
- [ ] Document the flag and its purpose in the readme. This is teaching
      content, not a vulnerability to fix.

## Phase 6: Documentation

- [ ] Update `CLAUDE.md` (projects table, Agent/MCP tools list, pgvector,
      new endpoints, seeding flag).
- [ ] Update `readme.md`'s feature list. Leave "Additional Tests to Create"
      alone, per CLAUDE.md.

## Suggested order and checkpoints

1. Phase 1 first. It is small and unlocks tool-call, usage, and latency
   visibility, which most demos need.
2. Phase 2 (now the largest phase: migration through MCP). Do 2a-2b
   (data, domain, API) and verify them before 2c-2d.
3. Phase 3 (biggest payoff; retrieval vs. response evaluation), then
   Phase 5, then Phase 4.

After each phase: `./test-with-coverage.ps1` green, and a manual smoke run of
the chat via `aspire run`.

## Risks

- **Package versions:** the course notes ask for exact versions and flagged
  previews. The vector-store and pgvector packages may be preview; record the
  versions used when adding them.
- **Shared mutable state in AppTests:** any new test that mutates data must
  respect the existing id exclusions and ordering.
- **Schema change:** the `AddOrderStatus` migration touches a shared model.
  AppTests and the AppHost run migrations against real Postgres, so run the
  full AppTests suite after Phase 2a, not just ApiTests.
- **Cancelling vs. clearing the cart:** done ahead of Phase 2. The UI button
  formerly labeled "Cancel Order / Clear Cart" is now "Clear Cart" (page
  handler `OnPostClearCart`), so "cancel" is reserved for placed orders and
  neither learners nor the model conflate the two.
- **Embeddings cost and key requirement:** ingest runs at startup/reset, so
  keep it cheap (small corpus) and skip gracefully without a key.
- **Image change:** moving `db` to a pgvector-enabled Postgres 18 image affects
  AppHost, the ApiTests Testcontainer, and CI. Verify CI before merging.
