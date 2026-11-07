namespace CarvedRock.ApiTests;

public class ProductApiTests : ApiTestsBase
{
    [Test]
    public async Task GetProductsAnonymous_ReturnsAllProducts()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync($"/product");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<List<Product>>();

        var randomProduct = TestData.GeneralFaker.PickRandom(TestData.InitialProducts);

        await Assert.That(content!)
            .Count().IsEqualTo(TestData.InitialProducts.Count)
            .And.Contains(p => p.Name == randomProduct.Name);
        // or details about first and last initial products?
    }

    [Test]
    [DependsOn(nameof(GetProductsLoggedIn_ReturnsAllProducts))]
    [DependsOn(nameof(GetProductsAnonymous_ReturnsAllProducts))]
    public async Task DeleteProductAsAdmin_Succeeds()
    {
        var client = Factory.CreateClient();
        client.AddAdminAuthHeaders();

        var response = await client.DeleteAsync("/product/1");
        // be careful - shared state and this deletes a product!

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task UpdateProductAsAdmin_Succeeds()
    {
        var client = Factory.CreateClient();
        client.AddAdminAuthHeaders();

        var lastProduct = TestData.InitialProducts.Last();

        var retrievedProduct = await client.GetFromJsonAsync<Product>
                                    ($"/product/{lastProduct.Id}");

        // update whichever fields you like
        var productToUpdate = new Product(
            0, // product id is on the path 
            Name: "Updated!",
            retrievedProduct!.Category,
            retrievedProduct!.Description,
            retrievedProduct!.Price,
            retrievedProduct!.ImgUrl);

        var response = await client.PutAsJsonAsync($"/product/{lastProduct.Id}",
                                        productToUpdate);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<Product>();
        await Assert.That(updated!.Name).IsEqualTo("Updated!");
    }

    [Test]
    public async Task GetProductsLoggedIn_ReturnsAllProducts()
    {
        var client = Factory.CreateClient();
        client.AddCustomerAuthHeaders();

        var response = await client.GetAsync($"/product");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadFromJsonAsync<List<Product>>();

        var randomProduct = TestData.GeneralFaker.PickRandom(TestData.InitialProducts);

        await Assert.That(content!)
            .Count().IsEqualTo(TestData.InitialProducts.Count)
            .And.Contains(p => p.Name == randomProduct.Name);
    }

    [Test]
    public async Task PostProductValidationFailure()
    {
        var client = Factory.CreateClient();
        client.AddAdminAuthHeaders();

        var newProduct = TestData.NewProductFaker.Generate();
        newProduct.Name = ""; // invalid

        var response = await client.PostAsJsonAsync
            ("/product", newProduct);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        await Assert.That(problemDetails).IsNotNull()
            .And.Member(pd => pd.Detail, detail =>
                    detail.IsEqualTo("One or more validation errors occurred."))
            .And.Member(pd => pd.Extensions.Keys, keys => keys.Contains("Name"))
            .And.Member(pd => pd.Extensions["Name"]!.ToString(),
                    err => err.Contains("Name is required."));
    }
}

// defined here to validate contract external callers may be 
// depending on specific property names / JSON format
public record Product(int Id, string Name, string Category,
                      string Description, double Price, string ImgUrl);