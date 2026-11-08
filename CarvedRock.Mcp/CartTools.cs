using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace CarvedRock.Mcp;

[Authorize] // any authenticated user (not admin-only) - cart is per-user
[McpServerToolType]
public class CartTools(IHttpClientFactory httpClientFactory)
{
    [McpServerTool(Name = "add_to_cart")]
    [Description("Add a product to the current signed-in user's cart, by product Id.")]
    public async Task<AdminTools.OperationResult> AddToCartAsync(int productId, int quantity = 1,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("CarvedRockApi");
        var response = await client.PostAsJsonAsync("Cart", new { productId, quantity }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error adding product {productId} to cart; HttpResponseCode was {(int)response.StatusCode}");

        return new AdminTools.OperationResult("ok");
    }

    [McpServerTool(Name = "clear_cart")]
    [Description("Remove every item from the signed-in user's cart (this does not cancel a placed order). " +
                 "This is destructive, so call it WITHOUT confirm first: nothing is removed and the result " +
                 "lists what would be removed so the user can be asked to confirm. Only after the user " +
                 "explicitly confirms in a later message, call it again with confirm=true.")]
    public async Task<AdminTools.OperationResult> ClearCartAsync(
        [Description("false (the default) only describes what would be removed. Pass true only after the " +
                     "user has explicitly confirmed clearing the cart.")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("CarvedRockApi");

        var cart = await client.GetFromJsonAsync<List<CartLine>>("Cart", cancellationToken) ?? [];
        if (cart.Count == 0)
            return new AdminTools.OperationResult("ok", "The cart is already empty.");

        if (!confirm)
            return new AdminTools.OperationResult("confirmation_required",
                $"The cart contains: {string.Join(", ", cart.Select(c => $"{c.Quantity} x {c.Name}"))}. " +
                "Clearing removes all of these. Ask the user to confirm, then call clear_cart again with confirm=true.");

        var response = await client.DeleteAsync("Cart", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error clearing cart; HttpResponseCode was {(int)response.StatusCode}");

        return new AdminTools.OperationResult("ok", "The cart has been cleared.");
    }
}
