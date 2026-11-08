using CarvedRock.Domain;

namespace CarvedRock.UnitTests;

public class KnowledgeChunkerTests
{
    private static KnowledgeDocument Doc(string markdown) => new("sample-doc", "Sample Doc", markdown);

    [Test]
    public async Task EachSecondLevelSectionBecomesAChunk()
    {
        var chunks = KnowledgeChunker.Chunk(Doc("""
            # Sample Doc

            ## First
            One.

            ## Second
            Two.
            """));

        await Assert.That(chunks.Select(c => c.Heading)).IsEquivalentTo(["First", "Second"]);
        await Assert.That(chunks.Select(c => c.Content)).IsEquivalentTo(["One.", "Two."]);
    }

    [Test]
    public async Task TextBeforeTheFirstSectionBecomesAnOverviewChunk()
    {
        var chunks = KnowledgeChunker.Chunk(Doc("""
            # Sample Doc

            Intro text.

            ## First
            One.
            """));

        await Assert.That(chunks[0].Heading).IsEqualTo(KnowledgeChunker.OverviewHeading);
        await Assert.That(chunks[0].Content).IsEqualTo("Intro text.");
    }

    [Test]
    public async Task ADocumentWithoutSectionsIsOneChunk()
    {
        var chunks = KnowledgeChunker.Chunk(Doc("# Sample Doc\n\nJust a paragraph."));

        await Assert.That(chunks.Count).IsEqualTo(1);
        await Assert.That(chunks[0].Heading).IsEqualTo(KnowledgeChunker.OverviewHeading);
    }

    [Test]
    public async Task EmptySectionsAndTheTitleLineAreSkipped()
    {
        var chunks = KnowledgeChunker.Chunk(Doc("# Sample Doc\n\n## Empty\n\n## Full\nText."));

        await Assert.That(chunks.Select(c => c.Heading)).IsEquivalentTo(["Full"]);
        await Assert.That(chunks.Any(c => c.Content.Contains("Sample Doc"))).IsFalse();
    }

    [Test]
    public async Task WindowsLineEndingsGiveTheSameChunksAsUnixOnes()
    {
        const string unix = "# Sample Doc\n\n## First\nOne.\n\n## Second\nTwo.";

        var fromUnix = KnowledgeChunker.Chunk(Doc(unix));
        var fromWindows = KnowledgeChunker.Chunk(Doc(unix.Replace("\n", "\r\n")));

        await Assert.That(fromWindows).IsEquivalentTo(fromUnix);
    }

    [Test]
    public async Task EmbeddingInputCarriesTheTitleAndHeadingForContext()
    {
        var chunk = KnowledgeChunker.Chunk(Doc("# Sample Doc\n\n## Sizing\nGo up a half size."))[0];

        await Assert.That(chunk.EmbeddingInput).Contains("Sample Doc");
        await Assert.That(chunk.EmbeddingInput).Contains("Sizing");
        await Assert.That(chunk.EmbeddingInput).Contains("Go up a half size.");
    }
}

public class KnowledgeCorpusTests
{
    [Test]
    public async Task TheEmbeddedCorpusLoadsWithUniqueIdsAndTitles()
    {
        var docs = KnowledgeCorpus.Load();

        await Assert.That(docs.Count).IsGreaterThanOrEqualTo(10);
        await Assert.That(docs.Select(d => d.Id).Distinct().Count()).IsEqualTo(docs.Count);
        await Assert.That(docs.All(d => !string.IsNullOrWhiteSpace(d.Title) && d.Title != d.Id)).IsTrue();
        await Assert.That(docs.Any(d => d.Id == "returns-and-exchanges")).IsTrue();
    }

    [Test]
    public async Task EveryDocumentYieldsChunksSmallEnoughToEmbed()
    {
        foreach (var doc in KnowledgeCorpus.Load())
        {
            var chunks = KnowledgeChunker.Chunk(doc);

            await Assert.That(chunks).IsNotEmpty();
            await Assert.That(chunks.All(c => c.EmbeddingInput.Length < 2_000)).IsTrue();
        }
    }

    [Test]
    public async Task TheCorpusIncludesAnArchivedPolicyThatConflictsWithTheCurrentOne()
    {
        var docs = KnowledgeCorpus.Load();

        var archived = docs.Single(d => d.Id == "returns-policy-archived-2023");
        await Assert.That(archived.Title).Contains("ARCHIVED");
        await Assert.That(archived.Markdown).Contains("60 days");
        await Assert.That(docs.Single(d => d.Id == "returns-and-exchanges").Markdown).Contains("30 days");
    }
}
