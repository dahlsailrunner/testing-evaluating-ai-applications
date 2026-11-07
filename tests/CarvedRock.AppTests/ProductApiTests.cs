namespace CarvedRock.AppTests;

[ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
public class ProductApiTests(AppFixture fixture)
{
    [Test]
    public async Task GetProductsAnonymous_ReturnsAllProducts()
    {
        var client = fixture.CreateHttpClient("api");

        var products = await client.GetFromJsonAsync<List<Product>>("/product");
        await Assert.That(products).Count().IsEqualTo(fixture.InitialProducts.Count);
    }

    [Test]
    public async Task GetProductsByIdAnonymous_ReturnsOk()
    {
        var client = fixture.CreateHttpClient("api");

        var product = await client.GetFromJsonAsync<Product>("/product/2");

        // Approach 1: check every property
        // await Assert.That(product)
        //     .Member(p => p.Name, name => name.IsEqualTo("Desert Walker"))
        //     .And.Member(p => p.Category, cat => cat.IsEqualTo("boots"))
        //     .And.Member(p => p.Price, price => price.IsEqualTo(74.99))
        //     .And.Member(p => p.ImgUrl, url => url.IsEqualTo("https://picsum.photos/id/15/800/600"))
        //     .And.Member(p => p.Description, desc => desc.EndsWith("desert exploration."));

        // approach 2: check an entire record that you create on the fly
        var expectedProduct = new Product(
                2,
                "Desert Walker",
                "boots",
                "Breathable and lightweight boots perfect for hot weather hiking and desert exploration.",
                74.99,
                "https://picsum.photos/id/15/800/600");
        await Assert.That(product).IsEqualTo(expectedProduct); // works because it's a record, not a class

        // approach 3: get the expected record from the initial data set
        var expected = fixture.InitialProducts.Single(p => p.Id == 2);
        await Assert.That(product).IsEqualTo(expected);
    }

    [Test]
    [DependsOn(nameof(GetProductsAnonymous_ReturnsAllProducts))]
    public async Task PostNewProductForAdmin_Works()
    {
        var client = await fixture.GetAdminApiClient();

        var newProduct = new NewProduct("Fancy Feet", "boots",
            "Amazing footwear!", 69.99, "https://some.img/cool.png");

        var response = await client.PostAsJsonAsync("/product", newProduct);
        await Assert.That(response.StatusCode).IsEqualTo(System.Net.HttpStatusCode.Created);

        var createdProduct = await response.Content.ReadFromJsonAsync<Product>();
        await Assert.That(createdProduct).IsNotNull()
            .And.Member(p => p.Name, name => name.IsEqualTo(newProduct.Name))
            .And.Member(p => p.Category, cat => cat.IsEqualTo(newProduct.Category))
            .And.Member(p => p.Description, desc => desc.IsEqualTo(newProduct.Description))
            .And.Member(p => p.Price, price => price.IsEqualTo(newProduct.Price))
            .And.Member(p => p.ImgUrl, url => url.IsEqualTo(newProduct.ImgUrl));
    }
}

public record NewProduct(string Name, string Category, string Description, double Price, string ImgUrl);