using Microsoft.Extensions.DependencyInjection;

namespace CarvedRock.ApiTests;

public class OrderApiTests : ApiTestsBase
{
    // These tests share one real database and run concurrently, so every test uses its own
    // fake user ("sub" claim) - that keeps carts and orders from interfering with each other.
    private HttpClient CustomerClient(string sub)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Authorization", $"Tester {sub}");
        client.DefaultRequestHeaders.Add("X-Test-sub", sub);
        client.DefaultRequestHeaders.Add("X-Test-idp", "CarvedRock");
        client.DefaultRequestHeaders.Add("X-Test-email", $"{sub}@someplace.com");
        return client;
    }

    private HttpClient AdminClient()
    {
        var client = Factory.CreateClient();
        client.AddAdminAuthHeaders();
        return client;
    }

    private RecordingOrderEmailSender Emails =>
        Factory.Services.GetRequiredService<RecordingOrderEmailSender>();

    private async Task<ApiOrder> PlaceOrderAsync(HttpClient client)
    {
        // ProductApiTests.DeleteProductAsAdmin_Succeeds deletes product 1 from the shared
        // database, so never pick it (same exclusion CartApiTests uses).
        var product = TestData.GeneralFaker.PickRandom(
            TestData.InitialProducts.Where(p => p.Id != 1));

        var cartResponse = await client.PostAsJsonAsync("/cart", new { productId = product.Id, quantity = 1 });
        await Assert.That(cartResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var orderResponse = await client.PostAsJsonAsync("/order", new { email = (string?)null });
        await Assert.That(orderResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);

        return (await orderResponse.Content.ReadFromJsonAsync<ApiOrder>())!;
    }

    private static async Task<ApiOrder> ReadOrder(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiOrder>())!;

    [Test]
    public async Task NewOrderStartsAsPlaced()
    {
        var order = await PlaceOrderAsync(CustomerClient("order-new"));

        await Assert.That(order.Status).IsEqualTo("Placed");
    }

    [Test]
    public async Task GetOrdersReturnsOnlyTheCallersOrders()
    {
        var alice = CustomerClient("order-list-a");
        var bob = CustomerClient("order-list-b");
        var aliceOrder = await PlaceOrderAsync(alice);
        await PlaceOrderAsync(bob);

        var orders = await alice.GetFromJsonAsync<List<ApiOrder>>("/order");

        await Assert.That(orders!.Select(o => o.Id)).IsEquivalentTo([aliceOrder.Id]);
    }

    [Test]
    public async Task OwnerCanGetOrderById()
    {
        var client = CustomerClient("order-get-owner");
        var placed = await PlaceOrderAsync(client);

        var response = await client.GetAsync($"/order/{placed.Id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ReadOrder(response)).Id).IsEqualTo(placed.Id);
    }

    [Test]
    public async Task GettingAnotherUsersOrderIsNotFound()
    {
        var placed = await PlaceOrderAsync(CustomerClient("order-get-victim"));

        var response = await CustomerClient("order-get-snoop").GetAsync($"/order/{placed.Id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AdminCanGetAnyOrder()
    {
        var placed = await PlaceOrderAsync(CustomerClient("order-get-admin-target"));

        var response = await AdminClient().GetAsync($"/order/{placed.Id}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task OwnerCanCancelPlacedOrderAndAnEmailIsSent()
    {
        var client = CustomerClient("order-cancel-owner");
        var placed = await PlaceOrderAsync(client);

        var response = await client.PostAsJsonAsync($"/order/{placed.Id}/cancel",
            new { reason = "ordered by mistake" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ReadOrder(response)).Status).IsEqualTo("Cancelled");

        var fetched = await client.GetFromJsonAsync<ApiOrder>($"/order/{placed.Id}");
        await Assert.That(fetched!.Status).IsEqualTo("Cancelled");

        await Assert.That(Emails.Sent.Count(e => e.Kind == "cancellation" && e.OrderId == placed.Id))
            .IsEqualTo(1);
    }

    [Test]
    public async Task CancelWorksWithoutARequestBody()
    {
        var client = CustomerClient("order-cancel-nobody");
        var placed = await PlaceOrderAsync(client);

        var response = await client.PostAsync($"/order/{placed.Id}/cancel", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task CancellingAnotherUsersOrderIsNotFoundAndLeavesItUntouched()
    {
        var owner = CustomerClient("order-cancel-victim");
        var placed = await PlaceOrderAsync(owner);

        var response = await CustomerClient("order-cancel-attacker")
            .PostAsJsonAsync($"/order/{placed.Id}/cancel", new { reason = "not mine" });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var fetched = await owner.GetFromJsonAsync<ApiOrder>($"/order/{placed.Id}");
        await Assert.That(fetched!.Status).IsEqualTo("Placed");
        await Assert.That(Emails.Sent.Any(e => e.Kind == "cancellation" && e.OrderId == placed.Id)).IsFalse();
    }

    [Test]
    public async Task CancellingTwiceIsAConflict()
    {
        var client = CustomerClient("order-cancel-twice");
        var placed = await PlaceOrderAsync(client);
        await client.PostAsync($"/order/{placed.Id}/cancel", content: null);

        var second = await client.PostAsync($"/order/{placed.Id}/cancel", content: null);

        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(Emails.Sent.Count(e => e.Kind == "cancellation" && e.OrderId == placed.Id))
            .IsEqualTo(1);
    }

    [Test]
    public async Task ShippedOrderCannotBeCancelled()
    {
        var client = CustomerClient("order-cancel-shipped");
        var placed = await PlaceOrderAsync(client);
        var shipResponse = await AdminClient().PostAsync($"/order/{placed.Id}/ship", content: null);
        await Assert.That(shipResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var response = await client.PostAsync($"/order/{placed.Id}/cancel", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        await Assert.That(problem!.Detail).Contains("shipped");
        var fetched = await client.GetFromJsonAsync<ApiOrder>($"/order/{placed.Id}");
        await Assert.That(fetched!.Status).IsEqualTo("Shipped");
    }

    [Test]
    public async Task AdminCanCancelAnotherUsersOrder()
    {
        var owner = CustomerClient("order-cancel-by-admin");
        var placed = await PlaceOrderAsync(owner);

        var response = await AdminClient().PostAsync($"/order/{placed.Id}/cancel", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var fetched = await owner.GetFromJsonAsync<ApiOrder>($"/order/{placed.Id}");
        await Assert.That(fetched!.Status).IsEqualTo("Cancelled");
    }

    [Test]
    public async Task CancellingUnknownOrderIsNotFound()
    {
        var response = await CustomerClient("order-cancel-unknown")
            .PostAsync("/order/999999/cancel", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task OverlongCancellationReasonIsABadRequest()
    {
        var client = CustomerClient("order-cancel-longreason");
        var placed = await PlaceOrderAsync(client);

        var response = await client.PostAsJsonAsync($"/order/{placed.Id}/cancel",
            new { reason = new string('x', 201) });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CustomerCannotMarkAnOrderShipped()
    {
        var client = CustomerClient("order-ship-customer");
        var placed = await PlaceOrderAsync(client);

        var response = await client.PostAsync($"/order/{placed.Id}/ship", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AdminCanMarkPlacedOrderShippedOnlyOnce()
    {
        var placed = await PlaceOrderAsync(CustomerClient("order-ship-admin"));
        var admin = AdminClient();

        var first = await admin.PostAsync($"/order/{placed.Id}/ship", content: null);
        var second = await admin.PostAsync($"/order/{placed.Id}/ship", content: null);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await ReadOrder(first)).Status).IsEqualTo("Shipped");
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task ShippingUnknownOrderIsNotFound()
    {
        var response = await AdminClient().PostAsync("/order/999999/ship", content: null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AdminCanListEveryCustomersOrders()
    {
        var first = await PlaceOrderAsync(CustomerClient("order-all-first"));
        var second = await PlaceOrderAsync(CustomerClient("order-all-second"));
        var admin = AdminClient();
        await admin.PostAsync($"/order/{second.Id}/ship", content: null);

        var all = await admin.GetFromJsonAsync<List<ApiOrder>>("/order/all");

        await Assert.That(all!.Select(o => o.Id)).Contains(first.Id);
        await Assert.That(all!.Single(o => o.Id == second.Id).Status).IsEqualTo("Shipped");
        // newest first
        await Assert.That(all!.Select(o => o.Id)).IsEquivalentTo(all!.Select(o => o.Id).OrderByDescending(i => i));
    }

    [Test]
    public async Task CustomerCannotListAllOrders()
    {
        var response = await CustomerClient("order-all-customer").GetAsync("/order/all");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task OrderEndpointsRequireAuthentication()
    {
        var anonymous = Factory.CreateClient();

        var response = await anonymous.GetAsync("/order");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}

public record ApiOrder(int Id, string Email, double Total, string Status);
