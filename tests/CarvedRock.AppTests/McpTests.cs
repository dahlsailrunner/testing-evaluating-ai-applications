using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace CarvedRock.AppTests;

[ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
public class McpTests(AppFixture fixture)
{
    public readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Test]
    public async Task GetToolsIncludesGetProducts()
    {
        var mcpClient = await fixture.GetAnonymousMcpClient();

        var tools = await mcpClient.ListToolsAsync();

        var getProductsTool = tools.FirstOrDefault(t => t.Name == "get_products");
        await Assert.That(getProductsTool).IsNotNull();

        var setPriceTool = tools.FirstOrDefault(t => t.Name == "set_product_price");
        await Assert.That(setPriceTool).IsNull();
    }

    [Test]
    public async Task CallGetProductsToolReturnsProducts()
    {
        var mcpClient = await fixture.GetAnonymousMcpClient();

        var getProductsResponse = await mcpClient.CallToolAsync("get_products");

        await Assert.That(getProductsResponse).IsNotNull()
            .And.Member(r => r.IsError, isErr => isErr.IsFalse().Or.IsNull());

        var productJson = getProductsResponse.Content
                        .First(c => c.Type == "text") as TextContentBlock;
        var products = JsonSerializer.Deserialize<List<ProductModel>>(
            productJson?.Text ?? "[]", CamelCaseOptions);

        await Assert.That(products).IsNotNull()
            .And.Contains(p => p.Name == "Alpine Trekker");
        // or check against fixture.InitialProducts
    }

    [Test]
    public async Task CallGetSingleProductToolReturnsProduct()
    {
        var mcpClient = await fixture.GetAnonymousMcpClient();

        // avoid ids mutated elsewhere in the suite (20/23 deleted, 22 price-updated)
        var expectedProduct = fixture.InitialProducts.First(p =>
            p.Id != 20 && p.Id != 22 && p.Id != 23);

        var response = await mcpClient.CallToolAsync("get_single_product",
            new Dictionary<string, object?> { { "id", expectedProduct.Id } });

        await Assert.That(response).IsNotNull()
            .And.Member(r => r.IsError, isErr => isErr.IsFalse().Or.IsNull());

        var productJson = response.Content.First(c => c.Type == "text") as TextContentBlock;
        var product = JsonSerializer.Deserialize<ProductModel>(
            productJson?.Text ?? "{}", CamelCaseOptions);

        await Assert.That(product).IsNotNull()
            .And.Member(p => p.Id, id => id.IsEqualTo(expectedProduct.Id))
            .And.Member(p => p.Name, n => n.IsEqualTo(expectedProduct.Name));
    }

    [Test]
    public async Task GetToolsAsAdminIncludesSetProductPrice()
    {
        var mcpClient = await fixture.GetAdminMcpClient();

        var tools = await mcpClient.ListToolsAsync();

        var setPriceTool = tools.FirstOrDefault(t => t.Name == "set_product_price");
        await Assert.That(setPriceTool).IsNotNull();

        var getProductsTool = tools.FirstOrDefault(t => t.Name == "get_products");
        await Assert.That(getProductsTool).IsNotNull();
    }

    [Test]
    public async Task SetPriceAsAdminWorks()
    {
        var mcpClient = await fixture.GetAdminMcpClient();

        var response = await mcpClient.CallToolAsync("set_product_price",
            new Dictionary<string, object?>
            {
                { "id", 22 },
                { "newPrice", 120.88 } // valid for all product types
            });

        var responseJson = response.Content.First(c => c.Type == "text") as TextContentBlock;
        var opResult = JsonSerializer.Deserialize<OperationResult>(responseJson?.Text ?? "{}",
            CamelCaseOptions);

        await Assert.That(opResult).IsNotNull()
            .And.Member(r => r.Status, s => s.IsEqualTo("ok"));
    }

    [Test]
    public async Task SetInvalidPriceAsAdminFails()
    {
        var mcpClient = await fixture.GetAdminMcpClient();

        var response = await mcpClient.CallToolAsync("set_product_price",
            new Dictionary<string, object?>
            {
                { "id", 22 },
                { "newPrice", 99_120.88 } // invalid for all product types
            });

        var responseJson = response.Content.First(c => c.Type == "text") as TextContentBlock;
        var opResult = JsonSerializer.Deserialize<OperationResult>(responseJson?.Text ?? "{}",
            CamelCaseOptions);

        await Assert.That(opResult).IsNotNull()
            .And.Member(r => r.Status, s => s.IsEqualTo("error"))
            .And.Member(r => r.Message, m => m.IsNotNull()
            .And.Contains("validation error"));
    }

    [Test]
    public async Task SetPriceToSameValueIsANoOp()
    {
        var mcpClient = await fixture.GetAdminMcpClient();

        // second candidate from the exclusion filter so this doesn't read the same
        // product as CallGetSingleProductToolReturnsProduct
        var targetProduct = fixture.InitialProducts
            .Where(p => p.Id != 20 && p.Id != 22 && p.Id != 23)
            .Skip(1).First();

        var currentProductResponse = await mcpClient.CallToolAsync("get_single_product",
            new Dictionary<string, object?> { { "id", targetProduct.Id } });
        var currentProductJson = currentProductResponse.Content
            .First(c => c.Type == "text") as TextContentBlock;
        var currentProduct = JsonSerializer.Deserialize<ProductModel>(
            currentProductJson?.Text ?? "{}", CamelCaseOptions);

        var response = await mcpClient.CallToolAsync("set_product_price",
            new Dictionary<string, object?>
            {
                { "id", targetProduct.Id },
                { "newPrice", currentProduct!.Price } // unchanged
            });

        var responseJson = response.Content.First(c => c.Type == "text") as TextContentBlock;
        var opResult = JsonSerializer.Deserialize<OperationResult>(responseJson?.Text ?? "{}",
            CamelCaseOptions);

        await Assert.That(opResult).IsNotNull()
            .And.Member(r => r.Status, s => s.IsEqualTo("not changed"));
    }
}
public record ProductModel(int Id, string Name, string Description, string Category, double Price);
public record OperationResult(string Status, string? Message = null);
