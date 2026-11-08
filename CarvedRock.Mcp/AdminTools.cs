using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace CarvedRock.Mcp;

[Authorize(Roles = "admin")]
[McpServerToolType]
public class AdminTools(IHttpClientFactory httpClientFactory)
{
    public record OperationResult(string Status, string? Message = null);

    [McpServerTool(Name = "delete_product")]
    [Description("Permanently delete a single product based on its Id. This is destructive, so call it " +
                 "WITHOUT confirm first: nothing is deleted and the result describes the product so the " +
                 "administrator can be asked to confirm. Only after the administrator explicitly confirms " +
                 "in a later message, call it again with confirm=true.")]
    public async Task<OperationResult> DeleteProductAsync(
        [Description("The id of the product to delete.")] int id,
        [Description("false (the default) only describes what would be deleted. Pass true only after the " +
                     "administrator has explicitly confirmed deleting this specific product.")] bool confirm = false,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("CarvedRockApi");

        // Look the product up first so the confirmation names exactly what would be deleted,
        // and an unknown id is reported without asking anyone to confirm anything.
        var lookup = await client.GetAsync($"product/{id}", cancellationToken);
        if (lookup.StatusCode == System.Net.HttpStatusCode.NotFound)
            return new OperationResult("error", $"Product {id} was not found.");
        if (!lookup.IsSuccessStatusCode)
            throw new Exception($"Error getting product {id}; HttpResponseCode was {(int)lookup.StatusCode}");

        var product = (await lookup.Content.ReadFromJsonAsync<FullProductModel>(cancellationToken))!;

        if (!confirm)
            return new OperationResult("confirmation_required",
                $"Product {id} \"{product.Name}\" ({product.Category}, ${product.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}) " +
                "will be permanently deleted. Ask the administrator to confirm, then call delete_product again with confirm=true.");

        var response = await client.DeleteAsync($"product/{id}", cancellationToken);
        if (!response.IsSuccessStatusCode) throw new Exception($"Error deleting product {id}; HttpResponseCode was {(int)response.StatusCode}");

        return new OperationResult("ok", $"Product {id} \"{product.Name}\" was deleted.");
    }


    [McpServerTool(Name = "set_product_price")]
    [Description("Update the price of a single product based on its Id.")]
    public async Task<OperationResult> UpdateProductPriceAsync(int id, double newPrice, CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("CarvedRockApi");

        var productToUpdate = await client.GetFromJsonAsync<FullProductModel>($"product/{id}", cancellationToken); // not found throws exception

        if (newPrice == productToUpdate!.Price)
            return new OperationResult("not changed", "new (provided) product price is same as current price");

        productToUpdate.Price = newPrice;

        var response = await client.PutAsJsonAsync($"product/{id}", productToUpdate, cancellationToken: cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var problem = JsonSerializer.Deserialize<ProblemDetails>(content);

            var errorDetails = "";
            if (problem != null)
            {
                errorDetails = problem.Detail;
                foreach (var (key, value) in problem.Extensions)
                {
                    errorDetails += $"; {key}: {value}";
                }
            }

            return new OperationResult("error", errorDetails);
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Error updating price on product {id}; HttpResponseCode was {(int)response.StatusCode}");
        }

        return new OperationResult("ok");
    }
}

public record FullProductModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public double Price { get; set; }
    public string Category { get; set; } = null!;
    public string ImgUrl { get; set; } = null!;
}