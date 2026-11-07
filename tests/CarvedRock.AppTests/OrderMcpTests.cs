using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CarvedRock.Data;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CarvedRock.AppTests;

// Order and cart MCP tools, exercised through the real MCP server against the real API and
// database. No LLM involved: these call the tools directly, the same way the agent would.
//
// Identities: orders are placed as the admin M2M client (m2m.short) and cancelled either by that
// same identity (the owner) or by the customer M2M client (m2m) to prove another user's order
// can't be touched. The customer's own cart is deliberately left alone because OrderTests places
// orders as m2m. Everything here shares m2m.short's cart, so the class runs serially.
[ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel("m2m-short-cart")]
public class OrderMcpTests(AppFixture fixture)
{
    // M2M tokens carry no email, so orders would otherwise all go to the same default address -
    // the one OrderTests looks in MailPit for. A dedicated address keeps these tests' emails
    // from landing in the middle of that test.
    private const string OrderEmail = "order-mcp-tests@carvedrock.com";

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static async Task<T> CallAsync<T>(McpClient client, string tool,
        Dictionary<string, object?>? args = null)
    {
        var response = await client.CallToolAsync(tool, args);
        var text = (TextContentBlock)response.Content.First(c => c.Type == "text");
        return JsonSerializer.Deserialize<T>(text.Text, CamelCase)!;
    }

    // avoid ids mutated elsewhere in the suite (20/23 deleted, 22 price-updated)
    private Product PickProduct() => fixture.GeneralFaker.PickRandom(
        fixture.InitialProducts.Where(p => p.Id != 20 && p.Id != 22 && p.Id != 23));

    private async Task<int> PlaceOrderAsAdminAsync()
    {
        var api = await fixture.GetAdminApiClient();

        var cartResponse = await api.PostAsJsonAsync("/cart", new CartItem(PickProduct().Id, 1));
        await Assert.That(cartResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var orderResponse = await api.PostAsJsonAsync("/order", new NewOrder(OrderEmail));
        await Assert.That(orderResponse.StatusCode).IsEqualTo(HttpStatusCode.Created);

        return (await orderResponse.Content.ReadFromJsonAsync<PlacedOrder>())!.Id;
    }

    private async Task<string> GetOrderStatusFromDatabaseAsync(int orderId)
    {
        var connStr = await fixture.App.GetConnectionStringAsync("CarvedRockPostgres");
        var options = new DbContextOptionsBuilder<LocalContext>().UseNpgsql(connStr).Options;
        await using var context = new LocalContext(options);

        var status = await context.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => o.Status)
            .SingleAsync();
        return status.ToString();
    }

    [Test]
    public async Task OrderAndCartToolsAreOnlyVisibleToSignedInUsers()
    {
        var anonymous = await (await fixture.GetAnonymousMcpClient()).ListToolsAsync();
        var customer = await (await fixture.GetCustomerMcpClient()).ListToolsAsync();

        string[] signedInOnly = ["get_my_orders", "get_order", "cancel_order", "clear_cart", "add_to_cart"];
        foreach (var name in signedInOnly)
        {
            await Assert.That(anonymous.Any(t => t.Name == name)).IsFalse();
            await Assert.That(customer.Any(t => t.Name == name)).IsTrue();
        }

        // admin-only tools stay hidden from customers
        await Assert.That(customer.Any(t => t.Name == "delete_product")).IsFalse();
    }

    [Test]
    public async Task GetMyOrdersListsThePlacedOrder()
    {
        var orderId = await PlaceOrderAsAdminAsync();
        var mcp = await fixture.GetAdminMcpClient();

        var orders = await CallAsync<List<McpOrder>>(mcp, "get_my_orders");

        var order = orders.Single(o => o.Id == orderId);
        await Assert.That(order.Status).IsEqualTo("Placed");
        await Assert.That(order.Details.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetOrderForSomeoneElsesOrderIsNotFound()
    {
        var orderId = await PlaceOrderAsAdminAsync();
        var customerMcp = await fixture.GetCustomerMcpClient(); // different user than the order's owner

        var lookup = await CallAsync<McpOrderLookup>(customerMcp, "get_order",
            new() { { "orderId", orderId } });

        await Assert.That(lookup.Status).IsEqualTo("not_found");
        await Assert.That(lookup.Order).IsNull();
    }

    [Test]
    public async Task CancelOrderWithoutConfirmDescribesTheOrderAndChangesNothing()
    {
        var orderId = await PlaceOrderAsAdminAsync();
        var mcp = await fixture.GetAdminMcpClient();

        var result = await CallAsync<OperationResult>(mcp, "cancel_order",
            new() { { "orderId", orderId } });

        await Assert.That(result.Status).IsEqualTo("confirmation_required");
        await Assert.That(result.Message).Contains($"Order {orderId}");
        await Assert.That(await GetOrderStatusFromDatabaseAsync(orderId)).IsEqualTo("Placed");
    }

    [Test]
    public async Task CancelOrderWithConfirmCancelsItAndSendsTheEmail()
    {
        var orderId = await PlaceOrderAsAdminAsync();
        var mcp = await fixture.GetAdminMcpClient();

        var result = await CallAsync<OperationResult>(mcp, "cancel_order",
            new() { { "orderId", orderId }, { "reason", "ordered by mistake" }, { "confirm", true } });

        await Assert.That(result.Status).IsEqualTo("ok");
        await Assert.That(await GetOrderStatusFromDatabaseAsync(orderId)).IsEqualTo("Cancelled");

        // the real EmailService (MailKit + template) sends the notice to MailPit
        await Assert.That(await CancellationEmailArrivedAsync(orderId)).IsTrue();
    }

    [Test]
    public async Task CustomerCannotCancelSomeoneElsesOrderEvenWithConfirm()
    {
        var orderId = await PlaceOrderAsAdminAsync();
        var customerMcp = await fixture.GetCustomerMcpClient();

        var result = await CallAsync<OperationResult>(customerMcp, "cancel_order",
            new() { { "orderId", orderId }, { "confirm", true } });

        await Assert.That(result.Status).IsEqualTo("error");
        await Assert.That(result.Message).Contains("No order");
        await Assert.That(await GetOrderStatusFromDatabaseAsync(orderId)).IsEqualTo("Placed");
    }

    [Test]
    public async Task ShippedOrderIsRefusedBeforeAnyConfirmationIsRequested()
    {
        var orderId = await PlaceOrderAsAdminAsync();
        var api = await fixture.GetAdminApiClient();
        var shipResponse = await api.PostAsync($"/order/{orderId}/ship", content: null);
        await Assert.That(shipResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var mcp = await fixture.GetAdminMcpClient();

        var result = await CallAsync<OperationResult>(mcp, "cancel_order",
            new() { { "orderId", orderId } });

        await Assert.That(result.Status).IsEqualTo("error"); // not "confirmation_required"
        await Assert.That(result.Message).Contains("shipped");
        await Assert.That(await GetOrderStatusFromDatabaseAsync(orderId)).IsEqualTo("Shipped");
    }

    [Test]
    public async Task ClearCartRequiresConfirmationAndThenEmptiesTheCart()
    {
        var api = await fixture.GetAdminApiClient();
        var product = PickProduct();
        await api.PostAsJsonAsync("/cart", new CartItem(product.Id, 1));
        var mcp = await fixture.GetAdminMcpClient();

        var firstCall = await CallAsync<OperationResult>(mcp, "clear_cart");

        await Assert.That(firstCall.Status).IsEqualTo("confirmation_required");
        await Assert.That(firstCall.Message).Contains(product.Name);
        var cartAfterFirst = await api.GetFromJsonAsync<List<CartLine>>("/cart");
        await Assert.That(cartAfterFirst).IsNotEmpty();

        var confirmed = await CallAsync<OperationResult>(mcp, "clear_cart", new() { { "confirm", true } });

        await Assert.That(confirmed.Status).IsEqualTo("ok");
        var cartAfterConfirm = await api.GetFromJsonAsync<List<CartLine>>("/cart");
        await Assert.That(cartAfterConfirm).IsEmpty();
    }

    // Polls MailPit briefly: the API sends the email as part of the cancel request, but delivery
    // to MailPit isn't guaranteed to be visible the instant the response returns.
    private async Task<bool> CancellationEmailArrivedAsync(int orderId)
    {
        using var mailClient = new HttpClient { BaseAddress = fixture.App.GetEndpoint("smtp", "http") };

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var messages = await mailClient.GetFromJsonAsync<MailPitMessageList>("/api/v1/messages");
            foreach (var summary in messages!.Messages
                         .Where(m => m.Subject == "Your CarvedRock Order Was Cancelled").Take(10))
            {
                var full = await mailClient.GetFromJsonAsync<MailPitMessage>($"/api/v1/message/{summary.ID}");
                if (Regex.IsMatch(full!.HTML, $@"#{orderId} has been cancelled")) return true;
            }
            await Task.Delay(500);
        }
        return false;
    }
}

public record McpOrderLine(int ProductId, string ProductName, int Quantity, double UnitPrice, double LineTotal);
public record McpOrder(int Id, string Status, double Total, List<McpOrderLine> Details);
public record McpOrderLookup(string Status, McpOrder? Order, string? Message);
