# Plan: AI Chat Recommends Products and Adds Them to Cart

## Feature request (verbatim)

> The AI chat accessible from the listing page should be able to add a
> recommended item or items to the cart. When the chat provides the
> recommendations, it should have a way to ask a follow up question about
> whether they would like any of the items added to the cart, and if the
> user says yes in some way, then the appropriate items should be added to
> the cart. Make sure that the text on the cart button is updated to
> reflect the added item. A Playwright test (with a video recording) should
> be created to verify the new functionality.

## Current state (why this needs more than a one-line tool add)

- The chat is **fully stateless** today. `CarvedRock.Agent/Agent.cs` builds a
  brand-new `AIAgent` on every `/agent` call and passes only the single
  latest `message` string — no history, no thread, no session. The MCP
  server is also explicitly `Stateless = true`. A naive "yes, add it" follow
  up has nothing to resolve "it" against.
- `CarvedRock.WebApp/Pages/Listing.cshtml` talks to the agent via a browser
  `EventSource` (GET only, no request body) hitting
  `Listing.cshtml.cs :: OnGetChat`, which relays bytes from the agent's
  `/agent` SSE-ish stream back to the browser as `text/event-stream`.
- The MCP server (`CarvedRock.Mcp`) only exposes read tools today
  (`get_products`, `get_single_product` in `CarvedRockTools.cs`) plus admin
  tools (`AdminTools.cs`). There is no tool that can mutate the cart.
- The cart itself (`CartController` in `CarvedRock.Api`) is `[Authorize]`
  and keyed by the JWT `sub` claim — there's no anonymous/guest cart, so
  any add-to-cart tool call must forward the real signed-in user's token
  (the same `TokenForwarder` pattern `CarvedRockTools`/`AdminTools` already
  use).
- The cart button (`Cart (@cartCount)` in `Pages/Shared/_Layout.cshtml`) is
  computed **server-side on full page render only** — there is no existing
  JS/AJAX path that updates it. Since chat responses stream in via SSE
  without a page reload, the button will not reflect a chat-driven add
  unless we add a small client-side refresh.

Because of the first bullet, this feature needs a little bit of plumbing
before it's "just add a tool" — the plan below calls that out explicitly so
you can push back on the approach before implementation starts.

## Design decisions (please sign off or redirect)

### 1. Conversation memory: client echoes history back to the server (chosen)

The browser already keeps the full visible transcript in the `#chatMessages`
DOM. Proposal: also keep a small in-memory JS array of
`{ role: "user" | "assistant", content: string }` turns for the lifetime of
the page, and send it alongside each new message. The Agent turns that into
a `List<ChatMessage>` (Microsoft.Extensions.AI) and passes the whole list to
`RunStreamingAsync` instead of a bare string, so the model actually sees
what it recommended last turn.

- **Why this over server-side session state**: no new storage, no cache
  eviction/cleanup, keeps the MCP server's intentional statelessness
  ("important for scaling", per its `Program.cs` comment) untouched, and
  conversation scope naturally resets when the user leaves/refreshes the
  listing page — which matches a "recommend, then confirm" chat widget well.
- **Trade-off**: history is lost on page refresh, and a chatty conversation
  sends more bytes per request. We'll cap it (proposed: last 10 turns) to
  bound token cost.
- **Alternative** (if you'd rather not touch the transport): store just the
  *last set of recommended product ids* server-side in `ISession` keyed by
  the ASP.NET cookie session id, instead of full history. Cheaper on the
  wire, but only supports "yes add them" for the immediately preceding
  recommendation, not richer follow-ups ("just the boots, not the jacket").

### 2. Chat transport: GET/EventSource → POST/fetch with manual SSE parsing

`EventSource` can't send a JSON body, so carrying history means either
cramming it into the query string or switching transports. Proposal: switch
the browser call to `fetch(..., { method: "POST", body: JSON.stringify({message, history}) })`
and manually read the streamed response via `response.body.getReader()`,
splitting on blank-line SSE frame boundaries — same incremental
`marked.parse(buffer)` rendering as today, just not using the `EventSource`
convenience wrapper. `OnGetChat` becomes `OnPostChat`, and the agent's
`/agent` minimal-API endpoint becomes a `POST` accepting a small JSON body
instead of a `message` query param.

- **Alternative**: keep GET + `EventSource`, JSON-encode a bounded history
  into the query string. Smaller diff, but fragile once conversations get
  long (URL length limits) and uglier to read in logs/dev tools.

### 3. New MCP tool: `add_to_cart`

New file `CarvedRock.Mcp/CartTools.cs`, same shape as `AdminTools.cs`:

```csharp
[Authorize] // any authenticated user — not admin-only
[McpServerToolType]
public class CartTools(IHttpClientFactory httpClientFactory)
{
    [McpServerTool(Name = "add_to_cart")]
    [Description("Add a product to the current user's cart by product id.")]
    public async Task<AdminTools.OperationResult> AddToCartAsync(
        int productId, int quantity = 1, CancellationToken cancellationToken = default)
    // POST Cart { productId, quantity } via the "CarvedRockApi" named client
    // (TokenForwarder already attaches the caller's bearer token)
}
```

No `Program.cs` changes needed — `WithToolsFromAssembly()` auto-discovers
new `[McpServerToolType]` classes. Reuses `AdminTools.OperationResult`
rather than duplicating the record.

- The tool is called **once per product** the model decides to add; the
  existing tool-calling loop already handles multiple sequential tool calls
  in one turn (proven by the existing `/admin delete products 20 and 23`
  test, which deletes two ids in a single chat turn).
- Anonymous chat is a non-issue in practice: `CarvedRock.WebApp`'s
  `Program.cs` already applies `RequireAuthorization()` to all Razor pages,
  so every chat session on the listing page is already authenticated.

### 4. System prompt: ask, then act

Update the default (non-admin) instructions built in `Agent.cs ::
GetPromptAsync` to something like:

> When you recommend products, end your reply by asking whether the user
> would like any of them added to their cart. If the user's next message
> confirms (yes / sure / please do / add them / etc.) for one or more
> previously recommended products, call `add_to_cart` once per confirmed
> product using its id and quantity 1. Only add products that were actually
> discussed in this conversation; ask for clarification if it's ambiguous
> which ones they mean.

### 5. Cart button refresh: re-fetch count after every chat turn

Proposal: after the SSE stream's `end` frame arrives in the browser, always
call a new lightweight handler `Listing?handler=CartCount` (returns
`{ count }` via `cartService.GetCartItemCountAsync()`) and set
`#carvedrockcart`'s text to `Cart (${count})`. Simple, idempotent, cheap —
avoids having to detect *which* tool calls happened by sniffing the model's
streamed output.

- **Alternative**: have the Agent emit a distinct SSE event (e.g.
  `event: cart-updated`) only when it actually detects an `add_to_cart`
  function-call result, so the browser only re-fetches when something
  changed. More precise, but couples the WebApp's SSE relay to
  `Microsoft.Extensions.AI`'s function-call update shape — more moving
  parts for a demo app. Recommend starting with the simpler always-refresh
  version above unless you want the more precise signal.

## Implementation checklist

### `CarvedRock.Mcp`
- [ ] Add `CartTools.cs` with the `[Authorize]` `add_to_cart` tool described above.

### `CarvedRock.Agent`
- [ ] `Agent.cs`: change `GetAgentResponse` to accept `(string message, IReadOnlyList<ChatTurn> history, CancellationToken)`, build a `List<ChatMessage>` from history + new message, and pass that list to `agent.RunStreamingAsync(...)` (confirm the exact overload on `AIAgent` during implementation — fall back to embedding a rendered transcript in the instructions if no message-list overload exists).
- [ ] `Agent.cs :: GetPromptAsync`: extend the default customer instructions per design decision #4.
- [ ] `Program.cs`: change the `/agent` minimal API from `MapGet` (query string `message`) to `MapPost` accepting a small JSON request body `{ message, history }`, still returning `IAsyncEnumerable<string>`.

### `CarvedRock.WebApp`
- [ ] `Listing.cshtml.cs`: rename/rework `OnGetChat` → `OnPostChat`, taking `[FromBody]` `{ message, history }`, POSTing that JSON to the agent's new endpoint, and continuing to relay the response as `text/event-stream` chunks (same relay loop as today).
- [ ] `Listing.cshtml.cs`: add `OnGetCartCount` returning `{ count }` JSON from `cartService.GetCartItemCountAsync()`.
- [ ] `Listing.cshtml` JS: maintain a bounded in-memory `chatHistory` array; replace the `EventSource` call with `fetch(...)` + manual `ReadableStream` parsing of the SSE-framed response, preserving today's incremental `marked.parse` rendering; on the `end` frame, append the turn to `chatHistory` and call the new `CartCount` handler to refresh `#carvedrockcart`'s text.
- [ ] No changes needed to `_Layout.cshtml` — the initial server-rendered count on full page loads stays as-is; only the JS-driven mid-session refresh is new.

## Testing plan

New Playwright test in `tests/CarvedRock.AppTests/WebAppTests.cs`, `[Test]` + `[RecordVideo]` (same convention as `AdminCanDeleteProductsViaChat` — no other setup needed, `CustomPageTest` handles the video dir/naming).

**User choice**: log in as **`bob`** (admin), not `alice`. `alice`'s cart is
shared mutable state across the existing `[DependsOn]`-chained order tests
(`CustomerCanPlaceOrderAndGetEmail` → `...FromCartPage` → `...FromCheckoutPage`);
`bob`'s cart is currently untouched by any other test (he's only used today
for the product-delete chat test, which doesn't touch the cart), so this new
test can run independently with no `[DependsOn]` ordering required.

**Determinism**: LLM wording and exactly which products it recommends
aren't guaranteed, so — consistent with the existing chat test's own
`// be careful - non-deterministic!!` comment — keep any assertion on the
model's prose soft, and put the hard assertion on the resulting DB row.
Steer the prompt at a specific, known product to avoid asserting on a
variable-length recommendation set, e.g. ask about a specific product by
name so the model's recommendation set is effectively fixed to one item.

Sketch:

```csharp
[Test]
[RecordVideo]
public async Task CustomerCanAddRecommendedProductToCartViaChat()
{
    await Page.GotoAsync(WebAppUrl);
    await Page.GetByRole(AriaRole.Link, new() { Name = "Sign in" }).ClickAsync();
    await Page.Login("bob", "bob");

    await Page.GetByRole(AriaRole.Link, new() { Name = "Footwear" }).ClickAsync();

    var chatInput = Page.GetByRole(AriaRole.Textbox, new() { Name = "Describe your activity" });
    var sendButton = Page.GetByRole(AriaRole.Button, new() { Name = "Send" });

    await chatInput.FillAsync("Can you recommend the Desert Walker boots?");
    await sendButton.ClickAsync();
    await Expect(Page.Locator("#chatMessages"))
        .ToContainTextAsync("Desert Walker", options: new() { Timeout = 15_000 });

    await chatInput.FillAsync("Yes, please add it to my cart");
    await sendButton.ClickAsync();
    await Expect(Page.Locator("#chatMessages"))
        .ToContainTextAsync("added", options: new() { Timeout = 15_000 }); // soft — wording varies

    // hard assertions: cart button text updates without a page reload, and the DB row exists
    await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Cart (1)" })).ToBeVisibleAsync();

    var product = await Fixture.TestDbContext.Products
        .FirstOrDefaultAsync(p => p.Name == "Desert Walker");
    var cartItem = await Fixture.TestDbContext.CartItems
        .FirstOrDefaultAsync(c => c.ProductId == product!.Id);
    await Assert.That(cartItem).IsNotNull();
}
```

- [ ] Add the test above (adjust product name to one that actually exists in `SeedData.json`/Bogus-seeded data — verify against `AppFixture`'s known products, not assumed).
- [ ] No `OrderTests.PlacingOrderWorksCompletely` exclusion-list update needed — this test mutates a cart row for `bob`, not product data, so it doesn't collide with the id-20/22/23 exclusions already documented in `CLAUDE.md`.
- [ ] Optional hygiene: clear bob's cart at the end of the test (mirrors the existing cancel-order tests' cleanup pattern) so repeated local runs against a long-lived DB start clean — not required for CI, which spins up a fresh Testcontainers Postgres per session.
- [ ] Run via `dotnet run --project tests/CarvedRock.AppTests --treenode-filter "/*/*/*/CustomerCanAddRecommendedProductToCartViaChat"` and confirm the `.webm` video lands in `playwright-artifacts/`.

## Risks / open questions

- Need to confirm during implementation which `AIAgent.RunStreamingAsync` overload accepts a message list/history (vs. a bare string) in the `Microsoft.Extensions.AI` version this repo pins — flagged above as a fallback-to-transcript-in-instructions option if no such overload exists.
- Non-deterministic LLM phrasing means the chat assertions are inherently softer than the DB assertions, same caveat the existing chat test already carries.
- History size is unbounded unless we cap it client-side (proposed: last 10 turns) — worth deciding a concrete number before implementing.
- If you'd rather not change the SSE transport (GET→POST), decision #2's query-string alternative avoids touching `OnGetChat`'s signature, at the cost of a length-limited history.

## Implementation notes (found while building and testing this)

Everything above was implemented as planned, plus two fixes discovered only by
actually running the new Playwright test repeatedly against the real app:

- **`AIAgent.RunStreamingAsync(IEnumerable<ChatMessage> messages, ...)` does
  exist** (confirmed by reflecting on `Microsoft.Agents.AI`, package
  `Microsoft.Agents.AI.OpenAI` 1.18.0 — `AIAgent` lives in `Microsoft.Agents.AI`,
  not `Microsoft.Extensions.AI`), so decision #1 needed no fallback.
- **The model hallucinated `productId=1` on every run**, regardless of which
  product was actually discussed (confirmed via the API's own
  `Adding product 1 to cart` log line, three runs in a row, all product 1
  while the conversation was about "Desert Walker"). Passing only the prior
  turns' *text* as history means the model never sees the exact numeric id
  from an earlier tool result, and it silently guessed a list position instead
  of re-checking. Fix: the system prompt now explicitly says *"Before calling
  add_to_cart, always call get_products or get_single_product first — even if
  you already discussed the product earlier — and use the exact numeric `id`
  field from that tool's result. Never guess an id, and never use a product's
  position in a list as its id."* This is the single most important prompt
  line for correctness — without it, the tool "succeeds" (204, no validation
  error) while adding the wrong product.
- **The test's `Cart (1)` assertion is not safe against reruns.** Unlike
  `CarvedRock.ApiTests`' per-session Testcontainers Postgres, the AppHost's own
  Postgres (used by `CarvedRock.AppTests`) is a persistent local container/volume
  that survives across separate `dotnet run` invocations — bob's cart carried
  a leftover item count from a previous run into the next one. The test now
  navigates to `/Cart` right after login and clicks "Cancel Order / Clear Cart"
  if it's present, before doing anything else, so it always starts from a known
  empty cart. Anyone adding another mutating AppTests test that isn't scoped to
  a fresh product id (the id-exclusion trick `OrderTests` uses) should assume
  the same persistence and reset state explicitly rather than relying on a
  fresh-per-session DB.
- Also needed: a header-based antiforgery token (`AntiforgeryOptions.HeaderName
  = "X-CSRF-TOKEN"`, exposed to the page via a `<meta>` tag, sent from the
  fetch call) since the chat POST body is JSON, not a form post — Razor Pages'
  default CSRF protection only looks at form fields/headers, not JSON bodies.
  `[IgnoreAntiforgeryToken]` was considered and rejected because it isn't
  honored on individual Razor Page handler methods (`MVC1001` — only the page
  class or a global convention) and applying it class-wide would have also
  weakened the existing `OnPostAddToCart` form handler.

Final verification: full `./test-with-coverage.ps1` run, 54/54 tests passing,
82.3% overall line coverage; the new chat-to-cart test was additionally run
several times back-to-back on its own to check for LLM-driven flakiness before
calling it done.
