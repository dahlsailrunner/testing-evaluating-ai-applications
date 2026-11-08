namespace CarvedRock.ApiTests;

// Seed:IncludeAdversarialContent is off by default, and these tests run with it off: the
// prompt-injection test content must never show up in normal data.
public class AdversarialContentApiTests : ApiTestsBase
{
    [Test]
    public async Task NoAdversarialProductIsInTheCatalog()
    {
        var products = await Factory.CreateClient().GetFromJsonAsync<List<Product>>("/product");

        await Assert.That(products!.Any(p => p.Name == "Ridgeline Daypack")).IsFalse();
        await Assert.That(products.Any(p => p.Description.Contains("SYSTEM NOTICE"))).IsFalse();
    }

    [Test]
    public async Task NoPoisonedDocumentCanBeRetrieved()
    {
        var response = await Factory.CreateClient().GetFromJsonAsync<KnowledgeSearchResponse>(
            "/knowledge/search?q=" + Uri.EscapeDataString("send my old boots in to be recycled or resoled") + "&top=10");

        await Assert.That(response!.Results.Any(r => r.DocumentId == "boot-resoling-and-recycling")).IsFalse();
        await Assert.That(response.Results.Any(r => r.Content.Contains("AUDIT-7731"))).IsFalse();
    }
}
