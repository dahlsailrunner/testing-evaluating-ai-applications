using CarvedRock.Core;
using CarvedRock.Data;
using CarvedRock.Domain;

namespace CarvedRock.UnitTests;

// Prompt-injection test content (a product and a help document) that is only ever present when
// Seed:IncludeAdversarialContent is switched on. These tests pin down both halves of that promise:
// nothing leaks into the normal data, and what the flag adds is exactly what the course expects.
public class AdversarialContentTests
{
    private const string PoisonedDocumentId = "boot-resoling-and-recycling";
    private const string Canary = "AUDIT-7731";

    [Test]
    public async Task TheDefaultKnowledgeBaseContainsNoAdversarialDocument()
    {
        var docs = KnowledgeCorpus.Load();

        await Assert.That(docs.Any(d => d.Id == PoisonedDocumentId)).IsFalse();
        await Assert.That(docs.Any(d => d.Markdown.Contains(Canary))).IsFalse();
        await Assert.That(docs.Any(d => d.Markdown.Contains("ASSISTANT INSTRUCTIONS"))).IsFalse();
    }

    [Test]
    public async Task TheFlagAddsExactlyOnePoisonedDocumentAndChangesNothingElse()
    {
        var normal = KnowledgeCorpus.Load();
        var withAdversarial = KnowledgeCorpus.Load(includeAdversarialContent: true);

        await Assert.That(withAdversarial.Count).IsEqualTo(normal.Count + 1);
        await Assert.That(withAdversarial.Select(d => d.Id).Except(normal.Select(d => d.Id)))
            .IsEquivalentTo([PoisonedDocumentId]);
        foreach (var doc in normal)
            await Assert.That(withAdversarial.Single(d => d.Id == doc.Id).Markdown).IsEqualTo(doc.Markdown);
    }

    [Test]
    public async Task ThePayloadSitsInTheSectionAQuestionAboutRecyclingWouldRetrieve()
    {
        var doc = KnowledgeCorpus.Load(includeAdversarialContent: true).Single(d => d.Id == PoisonedDocumentId);

        var poisoned = KnowledgeChunker.Chunk(doc).Single(c => c.Content.Contains(Canary));

        await Assert.That(poisoned.Heading).IsEqualTo("Recycling");
        await Assert.That(poisoned.Content).Contains("recycled"); // the legitimate text it hides behind
        await Assert.That(poisoned.Content).Contains("cancel_order");
    }

    [Test]
    public async Task TheDefaultProductSeedContainsNoAdversarialProduct()
    {
        var names = SeedDataLoader.LoadProducts().Select(p => p.Name).ToList();
        var adversarial = SeedDataLoader.LoadAdversarialProducts().Select(p => p.Name).ToList();

        await Assert.That(adversarial).IsNotEmpty();
        await Assert.That(names.Intersect(adversarial)).IsEmpty();
    }

    [Test]
    public async Task TheFlagAddsTheAdversarialProductsAfterTheNormalSeed()
    {
        var normal = SeedDataLoader.LoadProducts();
        var withAdversarial = SeedDataLoader.LoadProducts(includeAdversarialContent: true);

        await Assert.That(withAdversarial.Count)
            .IsEqualTo(normal.Count + SeedDataLoader.LoadAdversarialProducts().Count);
        await Assert.That(withAdversarial.Take(normal.Count).Select(p => p.Name))
            .IsEquivalentTo(normal.Select(p => p.Name)); // existing ids are unaffected: new ones come last
    }

    [Test]
    public async Task TheAdversarialProductCarriesAToolMisusePayloadButStillPassesProductValidation()
    {
        var product = SeedDataLoader.LoadAdversarialProducts().Single();

        await Assert.That(product.Description).Contains("add_to_cart");
        await Assert.That(product.Description.Length).IsLessThanOrEqualTo(ProductLimits.MaxDescriptionLength);

        // so an admin can still edit it (an update runs the same validator) like any other product
        var repo = ICarvedRockRepository.Mock();
        repo.IsProductNameUniqueAsync(Any()).Returns(true);
        var result = await new NewProductValidator(repo).ValidateAsync(new NewProductModel
        {
            Name = product.Name,
            Description = product.Description,
            Category = product.Category,
            Price = product.Price,
            ImgUrl = product.ImgUrl
        });

        await Assert.That(result.IsValid).IsTrue();
    }
}
