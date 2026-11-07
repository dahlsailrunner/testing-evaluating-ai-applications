# Testing and Evaluating AI Applications in .NET

This repo is meant to help with the understanding of automated testing
for AI applications in .NET.  Even AI applications will have some discrete
logic or functionality, and so traditional testing plays a part as well.

Key concepts for this repo and course will include

* "Happy path" or generally-normal use of AI features
* Adversarial use of AI features
* Reliability of results based on repeat usage or requests
* Leveraging the [Microsoft.Extensions.AI.Evaluation] testing library
* Assessment of multiple aspects of interactions, including groundedness, relevance, task
completion, tool call accuracy, and safety
* Tracking of call latency and cost -- both for the application and the evaluations
* Using deterministic or discrete testing wherever possible for specific output or results
* Understanding and defining acceptance thresholds
* Evaluating different models and if they behave differently with the same
prompts / interactions
* Integration into or definition of a CI pipeline that will execute
evaluation tests and report results
* Including tests and evaluations in agentic development workflows

## Getting Started

You need the [Aspire prerequisites](https://aspire.dev/get-started/prerequisites/).

### VS Code Setup

You need the following extension:

* [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)

Then just hit `F5` to run the app.

The [Aspire CLI](https://aspire.dev/get-started/install-cli/#install-the-aspire-cli) is highly recommended, along with the [Aspire VS Code Extension](https://aspire.dev/get-started/aspire-vscode-extension/).

### AI Setup

You also need to have an OpenAI key (use it to set the `openaiKey` parameter).
To set the parameter, you can run the application (just hit F5 from Visual Studio,
Rider, or VS Code) and you will be prompted to set that parameter, or use one
of the following commands from the root directory of the repo:

```posh
aspire secret set Parameters:openaiKey <your-openai-key>
```

or

```posh
dotnet user-secrets set "Parameters:openaiKey" "<your-openai-key>" --project .\CarvedRock.Agent\CarvedRock.Agent.csproj
```

### Running the Existing Tests

To run the tests that have already been created and look at the reports showing
the results and coverage, run the following command:

```bash
./test-with-coverage.ps1 -ShowReports
```

When this completes, three TUnit reports will open in the browser, as well as a code
coverage report.

Also, the `tests/CarvedRock.AppTests/bin/Debug/Net10.0/playwright-artifacts` folder
will have a screenshot and some videos that were captured during Playwright tests.

## Application Features

### UI

* The `Footwear`, `Equipment`, and `Kayaks` links will show listings of products
* The `Bad News` link will show an error page
* Authentication is required to all pages except the home page
* Products shown in listing pages can be added to the cart
* A shopping cart page shows products that have been added to the cart for the logged-in
  user
* The cart can be cleared
* Placing an order will save the order and send an email
* An `Admin` page is available for "bob" (the admin user)
* Navigating to `/admin` after logging in as `alice` will show an unauthorized page
* Product updates can be made on the admin page
* **An AI chat is on the product listing page that can interact with
the MCP server** (see below) -- it uses the `agent` API (this will have
functionality for both normal users and some things that are only
available to admins)

### API
  
* `GET /product` based on category (or "all") and by id allow anonymous requests
* `POST|PUT|DELETE /product` require authentication and an `admin` role (available with the `bob` login, but not `alice`)
* Validation with [FluentValidation](https://docs.fluentvalidation.net/en/latest/index.html) - try the `POST /product` method with a duplicate name or very high price
* A `GET /product` with a `category` of something other than "all", "boots", "equip", or "kayak" will throw an error
* An order can be placed with a `POST /order` request
* Shopping cart can be updated via API `/cart GET|POST|DEL`

### MCP

* Tools to get the list of products and to get a single product are available
  even to anonymous users
* Products can be added to the cart if the user requests
* A tool to delete a product is available to admin users
* A tool to update the product price is available to admin users

## Completing the Deterministic Tests

Here are some additional tests that should be created to improve
coverage - they were intentionally ommitted so that you could
practice with creating different types of tests. If you add tests
for everything below, your coverage should be pretty close to 100%.

### UnitTests

* Field length validations (e.g. name too long)
* Invalid `ImgUrl` property
* Negative price for a product

### ApiTests

* Use API to clear cart (both with contents and empty)
* Verify that get product by id returns `NOT FOUND` when invalid id is passed
* Same `NOT FOUND` response should be returned for update and delete operations with invalid id
* Determine if possible to create a validation error on a product that isn't
  tied to a field / property on the product.  If not, remove uncovered code
  in the `ValidationExceptionHandler`
* Force a bad request on an order

### AppTests

> [!TIP]
> Use the Playwright `codegen` tool for these!

* Check the Current Promotion page with the link area on the home
  page (will require user to log in when clicked)
* Click the `Bad News` button on the home page - this should redirect to
  an error page that can be validated
* Try the UI pages for the Admin of products (create, update, delete) - both
  with and without validation errors
* Log in as alice (not an admin). Confirm that the Admin nav button is not
  visible.  Also try navigating to `/admin` and verify the Access Denied page
  is shown
* Use the chat as `bob` the admin with `/admin set price of desert trekker to 104.21`
  and confirm the results (and probably verify that `alice` cannot do this)
* After logging in, log out and verify the logged out page

## Agentic Workflows

### Setup

Set up your favorite coding agent and initialize the repo for it - this usually
means you will end up with `claude.md` or `agents.md` in the root folder of the repo.

Then run the `aspire agent init` command from a terminal.  This will setup
the Aspire skills that an agent can take advantage of to understand and interact
with your app more completely and accurately.  For more information about this,
check the [Aspire docs on coding agents and workflows](https://aspire.dev/get-started/ai-coding-agents/).

Finally you probably should update your `claude.md` or `agents.md` to tell the
agent about the test setup - running the tests is a single script (`test-with-coverage.ps1`),
and coverage details can be seen in the cobertura files and test results in the
JSON files -- both in the `TestResults` directory.

### Planning and Implementing Features

Make sure that updated tests are part of the implementation plan for any
feature, and that running the updated tests is non-negotiable.  You can
even ask for any new Playwright tests to be recorded.

### Things to Try

#### Improve Test Coverage

* Improve coverage by addressing some or all of the [Completing the Deterministic Tests](#completing-the-deterministic-tests) above

#### Implement New Features

* Add admin ui for reviewing placed orders
* Add ability to see changes to products made by admins - audit log
* Add ability for admins to create promotions / sales / discounts temporarily for certain products
* Add support for guest (anonymous) checkout
* Make the chat widget more "global" once you're logged in - so that navigating
  from page to page and refreshing a page don't lose you current history

## Data and EF Core Migrations

The `dotnet ef` tool is used to manage EF Core migrations.  The following command was used to create migrations (from the `CarvedRock.Data` folder).

```bash
dotnet ef migrations add Initial -s ../CarvedRock.Api
```

The application uses PostgreSQL. Data is seeded by the `SeedData.json` contents in the `Data` project
