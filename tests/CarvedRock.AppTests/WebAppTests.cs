namespace CarvedRock.AppTests;

[ParallelLimiter<BrowserParallelLimit>]
public partial class WebAppTests : CustomPageTest
{
    [Test]
    public async Task HomePageWorks()
    {
        await Page.GotoAsync(WebAppUrl);

        await Page.ScreenshotAsync(new() { Path = "playwright-artifacts/screenshot.png" });

        await Expect(Page).ToHaveTitleAsync("Carved Rock Fitness");

        var bannerTextLocator = Page.GetByText("GET A GRIP");
        await Expect(bannerTextLocator).ToBeVisibleAsync();
    }

    [Test]
    [RecordVideo]
    public async Task CustomerCanPlaceOrderAndGetEmail()
    {
        await Page.GotoAsync(WebAppUrl);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Footwear" }).ClickAsync();

        // footwear link should redirect to login page
        await Page.Login("alice", "alice");  // customer

        await Page.GetByRole(AriaRole.Row, new() { Name = "Desert Walker" })
                    .GetByRole(AriaRole.Button).ClickAsync();
        await Page.GetByRole(AriaRole.Row, new() { Name = "River Guide" })
                    .GetByRole(AriaRole.Button).ClickAsync();

        // implicit assertion that the cart button shows 2 items in it
        await Page.GetByRole(AriaRole.Link, new() { Name = "Cart (2)" }).ClickAsync();

        await Expect(Page.Locator("tbody")).ToContainTextAsync("Desert Walker");
        await Expect(Page.Locator("tbody")).ToContainTextAsync("River Guide");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Checkout" })
                    .ClickAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit Order" })
                    .ClickAsync();

        await Expect(Page.Locator("h1"))
                .ToContainTextAsync("Thanks for your (fake) order!");

        var emailUrl = Fixture.App.GetEndpoint("smtp", "http").ToString();

        await Page.GotoAsync(emailUrl);

        await Page.GetByRole(AriaRole.Link, new() { Name = "to: alicesmith@email.com" })
                    .ClickAsync();

        // playwright assertions against the email
        await Expect(Page.Locator("#preview-html").ContentFrame
                    .Locator("body")).ToContainTextAsync("Desert Walker");
        await Expect(Page.Locator("#preview-html").ContentFrame
                    .Locator("body")).ToContainTextAsync("River Guide");

        await Expect(Page.Locator("#preview-html").ContentFrame
                    .GetByRole(AriaRole.Heading))
                    .ToContainTextAsync("Thank you for your order!");
        await Expect(Page.Locator("#preview-html").ContentFrame.Locator("body"))
                    .ToContainTextAsync("Enjoy your new gear!");
    }

    [Test]
    // alice's cart is shared backend state across every test that logs in as her -
    // run only after CustomerCanPlaceOrderAndGetEmail has cleared it via checkout.
    [DependsOn(nameof(CustomerCanPlaceOrderAndGetEmail))]
    public async Task CustomerCanCancelOrderFromCartPage()
    {
        await Page.GotoAsync(WebAppUrl);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Footwear" }).ClickAsync();

        await Page.Login("alice", "alice");

        await Page.GetByRole(AriaRole.Row, new() { Name = "Desert Walker" })
                    .GetByRole(AriaRole.Button).ClickAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Cart (1)" }).ClickAsync();

        await Expect(Page.Locator("tbody")).ToContainTextAsync("Desert Walker");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel Order / Clear Cart" })
                    .ClickAsync();

        await Expect(Page.GetByText("GET A GRIP")).ToBeVisibleAsync(); // redirected home
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Cart (0)" })).ToBeVisibleAsync();
    }

    [Test]
    [DependsOn(nameof(CustomerCanCancelOrderFromCartPage))]
    public async Task CustomerCanCancelOrderFromCheckoutPage()
    {
        await Page.GotoAsync(WebAppUrl);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Footwear" }).ClickAsync();

        await Page.Login("alice", "alice");

        await Page.GetByRole(AriaRole.Row, new() { Name = "Desert Walker" })
                    .GetByRole(AriaRole.Button).ClickAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Cart (1)" }).ClickAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Checkout" }).ClickAsync();

        await Expect(Page.Locator("tbody")).ToContainTextAsync("Desert Walker");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel Order / Clear Cart" })
                    .ClickAsync();

        await Expect(Page.GetByText("GET A GRIP")).ToBeVisibleAsync(); // redirected home
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Cart (0)" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task NavigatingToListingWithoutCategoryShowsErrorPage()
    {
        // no "cat" query string - distinct from the "Bad News" nav link (cat=badnews),
        // which is its own exercise; this hits Listing.cshtml.cs's other error path
        var listingUrl = new Uri(new Uri(WebAppUrl), "Listing").ToString();

        await Page.GotoAsync(listingUrl);
        await Page.Login("alice", "alice"); // page requires auth; login redirects back here

        await Expect(Page.GetByText("An error occurred while processing your request."))
                    .ToBeVisibleAsync();
    }

    [Test]
    [RecordVideo]
    public async Task AdminCanDeleteProductsViaChat()
    {
        await Page.GotoAsync(WebAppUrl);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Sign in" }).ClickAsync();

        await Page.Login("bob", "bob");  // admin

        await Page.GetByRole(AriaRole.Link, new() { Name = "Footwear" }).ClickAsync();

        await Page.GetByRole(AriaRole.Textbox, new() { Name = "Describe your activity" })
                        .FillAsync("/admin delete products 20 and 23");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        await Expect(Page.Locator("#chatMessages"))
                .ToContainTextAsync("successfully",  // be careful - non-deterministic!!
                    options: new() { Timeout = 15_000 });

        var actualProduct = await Fixture.TestDbContext.Products
                                .FirstOrDefaultAsync(p => p.Id == 20 || p.Id == 23);
        await Assert.That(actualProduct).IsNull();
    }

    [Test]
    [RecordVideo]
    // bob (admin), not alice - alice's cart is shared state across the DependsOn chain above;
    // bob's cart isn't touched by any other test, so this one needs no ordering.
    public async Task CustomerCanAddRecommendedProductToCartViaChat()
    {
        await Page.GotoAsync(WebAppUrl);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Sign in" }).ClickAsync();

        await Page.Login("bob", "bob");

        // bob's cart is real, persistent state (unlike ApiTests' per-session Testcontainers DB,
        // this AppHost's Postgres survives across separate test runs) - start from a known-empty
        // cart so the "Cart (1)" assertion below is reliable no matter how many times this ran before.
        await Page.GotoAsync(new Uri(new Uri(WebAppUrl), "Cart").ToString());
        var clearCartButton = Page.GetByRole(AriaRole.Button, new() { Name = "Cancel Order / Clear Cart" });
        if (await clearCartButton.IsVisibleAsync())
        {
            await clearCartButton.ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Cart (0)" })).ToBeVisibleAsync();
        }

        await Page.GetByRole(AriaRole.Link, new() { Name = "Footwear" }).ClickAsync();

        var chatInput = Page.GetByRole(AriaRole.Textbox, new() { Name = "Describe your activity" });
        var sendButton = Page.GetByRole(AriaRole.Button, new() { Name = "Send" });

        await chatInput.FillAsync("Tell me about the Desert Walker boots.");
        await sendButton.ClickAsync();
        await Expect(Page.Locator("#chatMessages"))
                .ToContainTextAsync("Desert Walker", options: new() { Timeout = 15_000 });

        await chatInput.FillAsync("Yes, please add the Desert Walker to my cart.");
        await sendButton.ClickAsync();
        await Expect(Page.Locator("#chatMessages"))
                .ToContainTextAsync("added",  // be careful - non-deterministic wording!!
                    options: new() { Timeout = 15_000 });

        // hard assertions: the cart button text updates without a page reload, and the DB row exists
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Cart (1)" }))
                .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var desertWalker = await Fixture.TestDbContext.Products
                                .FirstOrDefaultAsync(p => p.Name == "Desert Walker");
        await Assert.That(desertWalker).IsNotNull();

        var cartItem = await Fixture.TestDbContext.CartItems
                                .FirstOrDefaultAsync(c => c.ProductId == desertWalker!.Id);
        await Assert.That(cartItem).IsNotNull();
    }
}
