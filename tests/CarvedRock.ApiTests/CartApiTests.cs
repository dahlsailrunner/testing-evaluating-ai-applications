namespace CarvedRock.ApiTests;

public class CartApiTests : ApiTestsBase
{
    [Test]
    public async Task GetEmptyCartWorks()
    {
        var client = Factory.CreateClient();
        client.AddCustomerAuthHeaders();

        var response = await client.GetAsync("/cart");
        var jsonContent = await response.Content.ReadAsStringAsync();

        // var logger = TestContext.Current!.GetDefaultLogger();
        TestLogger.LogInformation($"Got the following response: --{jsonContent}--");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(jsonContent).IsEqualTo("[]"); // empty array
    }

    [Test]
    public async Task AddToCartWorks()
    {
        var client = Factory.CreateClient();
        client.AddCustomerAuthHeaders();

        // TestData.InitialProducts is a session-wide snapshot taken before any test runs;
        // ProductApiTests.DeleteProductAsAdmin_Succeeds deletes product 1 from the real
        // (shared) database, so exclude it here or a random pick can 404 the POST.
        var randomProduct = TestData.GeneralFaker.PickRandom(
            TestData.InitialProducts.Where(p => p.Id != 1));

        var response = await client.PostAsJsonAsync("/cart",
                                new CartItem(randomProduct.Id, 1));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var cartCountResponse = await client.GetAsync("/cart/count");
        await Assert.That(cartCountResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var cartCount = await cartCountResponse.Content.ReadAsStringAsync();
        await Assert.That(Convert.ToInt32(cartCount)).IsEqualTo(1);

        // for you: assert about cart contents - different api call
    }

    [Test]
    // TUnit runs tests in this class concurrently by default; without this, the extra
    // thread-pool contention from this test occasionally lets AddToCartWorks's mutation
    // land before GetEmptyCartWorks reads the (supposedly still-empty) cart.
    [DependsOn(nameof(GetEmptyCartWorks))]
    public async Task AddingSameProductTwiceIncrementsQuantity()
    {
        var client = Factory.CreateClient();
        // AddCustomerAuthHeaders() always maps to the same "customer" user, whose cart
        // GetEmptyCartWorks/AddToCartWorks above also mutate - use a distinct fake user
        // (own "sub" claim) so this test's cart can't race with theirs.
        client.DefaultRequestHeaders.Add("X-Authorization", "Increment Tester");
        client.DefaultRequestHeaders.Add("X-Test-sub", "increment-tester");
        client.DefaultRequestHeaders.Add("X-Test-idp", "CarvedRock");
        client.DefaultRequestHeaders.Add("X-Test-email", "incrementtester@someplace.com");

        // TestData.InitialProducts is a session-wide snapshot taken before any test runs;
        // ProductApiTests.DeleteProductAsAdmin_Succeeds deletes product 1 from the real
        // (shared) database, so exclude it here or a random pick can 404 the POST.
        var randomProduct = TestData.GeneralFaker.PickRandom(
            TestData.InitialProducts.Where(p => p.Id != 1));

        await client.PostAsJsonAsync("/cart", new CartItem(randomProduct.Id, 1));
        var secondResponse = await client.PostAsJsonAsync("/cart", new CartItem(randomProduct.Id, 2));

        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        var cartCountResponse = await client.GetAsync("/cart/count");
        var cartCount = await cartCountResponse.Content.ReadAsStringAsync();
        // same product line incremented (1 + 2), not a second line
        await Assert.That(Convert.ToInt32(cartCount)).IsEqualTo(3);
    }
}

public record CartItem(int ProductId, int Quantity);