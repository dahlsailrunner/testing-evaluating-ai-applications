namespace CarvedRock.AppTests;

// "My Orders" page (user menu > My Orders): list every order with a status badge and cancel
// the ones that are still Placed. These run as bob and share his cart with
// CustomerCanAddRecommendedProductToCartViaChat, hence the shared NotInParallel key.
public partial class WebAppTests
{
    private async Task SignInAsBobWithEmptyCartAsync()
    {
        await Page.GotoAsync(WebAppUrl);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Sign in" }).ClickAsync();
        await Page.Login("bob", "bob");

        // start from a known-empty cart (bob's cart is persistent shared state, see
        // CustomerCanAddRecommendedProductToCartViaChat) so the cart count assertions are reliable
        await Page.GotoAsync(new Uri(new Uri(WebAppUrl), "Cart").ToString());
        var clearCartButton = Page.GetByRole(AriaRole.Button, new() { Name = "Clear Cart", Exact = true });
        if (await clearCartButton.IsVisibleAsync())
        {
            await clearCartButton.ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Cart (0)" })).ToBeVisibleAsync();
        }
    }

    private async Task PlaceOrderViaUiAsync(string productName)
    {
        await Page.GetByRole(AriaRole.Link, new() { Name = "Footwear" }).ClickAsync();
        await Page.GetByRole(AriaRole.Row, new() { Name = productName })
                    .GetByRole(AriaRole.Button).ClickAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "Cart (1)" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Checkout" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Submit Order" }).ClickAsync();

        await Expect(Page.Locator("h1")).ToContainTextAsync("Thanks for your (fake) order!");
    }

    private async Task OpenMyOrdersFromMenuAsync()
    {
        await Page.Locator("#userMenu").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Sign Out" })).ToBeVisibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "My Orders" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My Orders" })).ToBeVisibleAsync();
    }

    // Orders are listed newest first, so the first row containing the product is the order
    // this test just placed - even if an earlier attempt of the same test left another behind.
    private static async Task<(ILocator Row, int OrderId)> FindNewestOrderRowAsync(IPage page, string productName)
    {
        var row = page.GetByRole(AriaRole.Row).Filter(new() { HasText = productName }).First;
        var orderNumber = await row.Locator("td").First.InnerTextAsync(); // "#12"
        return (row, int.Parse(orderNumber.TrimStart('#')));
    }

    [Test]
    [RecordVideo]
    [NotInParallel("bob-cart")]
    public async Task CustomerCanCancelPlacedOrderFromMyOrdersPage()
    {
        await SignInAsBobWithEmptyCartAsync();
        await PlaceOrderViaUiAsync("Mountain Summit");
        await OpenMyOrdersFromMenuAsync();

        var (row, orderId) = await FindNewestOrderRowAsync(Page, "Mountain Summit");
        await Expect(row).ToContainTextAsync("Placed");

        await row.GetByRole(AriaRole.Button, new() { Name = "Cancel order" }).ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog).ToContainTextAsync($"Cancel order #{orderId}?");
        await dialog.GetByLabel("Reason (optional)").FillAsync("Changed my mind");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Yes, cancel order" }).ClickAsync();

        await Expect(Page.Locator("#order-status-message"))
                .ToContainTextAsync($"Order #{orderId} has been cancelled.");
        var updatedRow = Page.Locator($"#order-{orderId}");
        await Expect(updatedRow).ToContainTextAsync("Cancelled");
        await Expect(updatedRow.GetByRole(AriaRole.Button, new() { Name = "Cancel order" }))
                .ToHaveCountAsync(0);

        await using var db = await Fixture.CreateDbContextAsync();
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        await Assert.That(order.Status.ToString()).IsEqualTo("Cancelled");
        await Assert.That(order.CancellationReason).IsEqualTo("Changed my mind");
    }

    [Test]
    [RecordVideo]
    [NotInParallel("bob-cart")]
    public async Task CancellingAnOrderThatShippedAfterThePageLoadedShowsAnError()
    {
        await SignInAsBobWithEmptyCartAsync();
        await PlaceOrderViaUiAsync("Wetland Wader");
        await OpenMyOrdersFromMenuAsync();

        var (row, orderId) = await FindNewestOrderRowAsync(Page, "Wetland Wader");
        await Expect(row.GetByRole(AriaRole.Button, new() { Name = "Cancel order" })).ToBeVisibleAsync();

        // meanwhile the order ships (admin-only API call), so the page is now out of date
        var adminApi = await Fixture.GetAdminApiClient();
        var shipResponse = await adminApi.PostAsync($"/order/{orderId}/ship", content: null);
        await Assert.That(shipResponse.StatusCode).IsEqualTo(System.Net.HttpStatusCode.OK);

        await row.GetByRole(AriaRole.Button, new() { Name = "Cancel order" }).ClickAsync();
        await Page.GetByRole(AriaRole.Dialog)
                .GetByRole(AriaRole.Button, new() { Name = "Yes, cancel order" }).ClickAsync();

        var message = Page.Locator("#order-status-message");
        await Expect(message).ToContainTextAsync("already shipped");
        await Expect(message).ToHaveClassAsync(new Regex("alert-danger"));

        // the refreshed list shows the real status, with no cancel button
        var shippedRow = Page.Locator($"#order-{orderId}");
        await Expect(shippedRow).ToContainTextAsync("Shipped");
        await Expect(shippedRow.GetByRole(AriaRole.Button, new() { Name = "Cancel order" }))
                .ToHaveCountAsync(0);
    }
}
