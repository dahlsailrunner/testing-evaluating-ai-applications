using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;

namespace CarvedRock.Mcp;

// Any signed-in user; the API only ever returns/changes the caller's own orders (the bearer
// token is forwarded by TokenForwarder), so these tools can't see anyone else's.
[Authorize]
[McpServerToolType]
public class OrderTools(IHttpClientFactory httpClientFactory)
{
    private const int MaxReasonLength = 200;

    [McpServerTool(Name = "get_my_orders")]
    [Description("Get the signed-in user's orders, newest first. Each order has an id, date, status " +
                 "(Placed, Shipped or Cancelled), total and its line items.")]
    public async Task<List<OrderSummary>> GetMyOrdersAsync(CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("CarvedRockApi");
        return await client.GetFromJsonAsync<List<OrderSummary>>("/order", cancellationToken) ?? [];
    }

    [McpServerTool(Name = "get_order")]
    [Description("Get one of the signed-in user's orders by its order id.")]
    public async Task<OrderLookup> GetOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("CarvedRockApi");
        var response = await client.GetAsync($"/order/{orderId}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new OrderLookup("not_found", Message: NotFoundMessage(orderId));

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error getting order {orderId}; HttpResponseCode was {(int)response.StatusCode}");

        var order = await response.Content.ReadFromJsonAsync<OrderSummary>(cancellationToken);
        return new OrderLookup("ok", order);
    }

    [McpServerTool(Name = "cancel_order")]
    [Description("Cancel one of the signed-in user's orders. Only orders that have not shipped can be " +
                 "cancelled. This is destructive, so call it WITHOUT confirm first: nothing is cancelled " +
                 "and the result describes the order so the user can be asked to confirm. Only after the " +
                 "user explicitly confirms in a later message, call it again with confirm=true.")]
    public async Task<AdminTools.OperationResult> CancelOrderAsync(
        [Description("The id of the order to cancel.")] int orderId,
        [Description("Optional reason the customer gave for cancelling (200 characters max).")] string? reason = null,
        [Description("false (the default) only describes what would be cancelled. Pass true only after " +
                     "the user has explicitly confirmed the cancellation of this specific order.")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0)
            return new AdminTools.OperationResult("error", "orderId must be a positive order id.");
        if (reason is { Length: > MaxReasonLength })
            return new AdminTools.OperationResult("error", $"reason is too long (maximum {MaxReasonLength} characters).");

        var client = httpClientFactory.CreateClient("CarvedRockApi");

        // Look the order up first. A refusal (not found / not yours / already shipped or
        // cancelled) is reported straight away instead of asking the user to confirm
        // something that couldn't happen anyway.
        var lookup = await client.GetAsync($"/order/{orderId}", cancellationToken);
        if (lookup.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new AdminTools.OperationResult("error", NotFoundMessage(orderId));
        if (!lookup.IsSuccessStatusCode)
            throw new Exception($"Error getting order {orderId}; HttpResponseCode was {(int)lookup.StatusCode}");

        var order = (await lookup.Content.ReadFromJsonAsync<OrderSummary>(cancellationToken))!;
        if (order.Status != "Placed")
            return new AdminTools.OperationResult("error", order.Status == "Shipped"
                ? $"Order {orderId} has already shipped and can no longer be cancelled; it would need to be returned instead."
                : $"Order {orderId} has already been cancelled.");

        if (!confirm)
            return new AdminTools.OperationResult("confirmation_required",
                $"Order {orderId} (placed {order.OrderDate:yyyy-MM-dd}): {Describe(order)}; total ${Money(order.Total)}. " +
                "Cancelling is permanent and the customer is notified by email. " +
                "Ask the user to confirm, then call cancel_order again with confirm=true.");

        var response = await client.PostAsJsonAsync($"/order/{orderId}/cancel",
            new { reason }, cancellationToken);

        switch (response.StatusCode)
        {
            case System.Net.HttpStatusCode.OK:
                return new AdminTools.OperationResult("ok", $"Order {orderId} has been cancelled.");
            case System.Net.HttpStatusCode.NotFound:
                return new AdminTools.OperationResult("error", NotFoundMessage(orderId));
            case System.Net.HttpStatusCode.Conflict:
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
                return new AdminTools.OperationResult("error", problem?.Detail ?? $"Order {orderId} could not be cancelled.");
            default:
                throw new Exception($"Error cancelling order {orderId}; HttpResponseCode was {(int)response.StatusCode}");
        }
    }

    // Same wording whether the order doesn't exist or belongs to someone else.
    private static string NotFoundMessage(int orderId) =>
        $"No order {orderId} was found for the signed-in user.";

    private static string Describe(OrderSummary order) =>
        string.Join(", ", order.Details.Select(d => $"{d.Quantity} x {d.ProductName}"));

    private static string Money(double amount) => amount.ToString("F2", CultureInfo.InvariantCulture);
}
