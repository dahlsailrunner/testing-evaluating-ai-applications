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
}
