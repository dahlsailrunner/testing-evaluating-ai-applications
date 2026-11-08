using CarvedRock.Data;
using CarvedRock.Data.Entities;
using CarvedRock.Domain;
using CarvedRock.UnitTests.Utils;
using Microsoft.Extensions.Logging.Abstractions;

namespace CarvedRock.UnitTests;

public class KnowledgeLogicTests
{
    private static KnowledgeDocument Doc(string id, params (string Heading, string Body)[] sections) =>
        new(id, $"Title of {id}",
            $"# Title of {id}\n\n" + string.Join("\n\n", sections.Select(s => $"## {s.Heading}\n{s.Body}")));

    private static KnowledgeLogic LogicFor(InMemoryKnowledgeRepository repo, CountingEmbeddingGenerator model) =>
        new(repo, model, NullLogger<KnowledgeLogic>.Instance);

    [Test]
    public async Task FirstIngestEmbedsEveryChunkInOneBatch()
    {
        var repo = new InMemoryKnowledgeRepository();
        var model = new CountingEmbeddingGenerator();

        var summary = await LogicFor(repo, model).IngestAsync(
            [Doc("a", ("One", "alpha"), ("Two", "beta")), Doc("b", ("Three", "gamma"))]);

        await Assert.That(summary).IsEqualTo(new IngestionSummary(Added: 3, Removed: 0, Unchanged: 0));
        await Assert.That(model.Calls.Count).IsEqualTo(1);
        await Assert.That(model.TotalInputs).IsEqualTo(3);
        await Assert.That(repo.Stored.Count).IsEqualTo(3);
    }

    [Test]
    public async Task IngestingUnchangedDocumentsAgainDoesNotCallTheModel()
    {
        var repo = new InMemoryKnowledgeRepository();
        var model = new CountingEmbeddingGenerator();
        var logic = LogicFor(repo, model);
        KnowledgeDocument[] docs = [Doc("a", ("One", "alpha"), ("Two", "beta"))];
        await logic.IngestAsync(docs);

        var second = await logic.IngestAsync(docs);

        await Assert.That(second).IsEqualTo(new IngestionSummary(0, 0, Unchanged: 2));
        await Assert.That(model.Calls.Count).IsEqualTo(1); // only the first run
        await Assert.That(repo.Stored.Count).IsEqualTo(2);
    }

    [Test]
    public async Task OnlyTheChangedSectionIsReEmbeddedAndItsOldChunkRemoved()
    {
        var repo = new InMemoryKnowledgeRepository();
        var model = new CountingEmbeddingGenerator();
        var logic = LogicFor(repo, model);
        await logic.IngestAsync([Doc("a", ("One", "alpha"), ("Two", "beta"))]);

        var summary = await logic.IngestAsync([Doc("a", ("One", "alpha"), ("Two", "beta, but edited"))]);

        await Assert.That(summary).IsEqualTo(new IngestionSummary(Added: 1, Removed: 1, Unchanged: 1));
        await Assert.That(model.Calls[1].Count).IsEqualTo(1);
        await Assert.That(repo.Stored.Select(s => s.Chunk.Content)).Contains("beta, but edited");
        await Assert.That(repo.Stored.Select(s => s.Chunk.Content)).DoesNotContain("beta");
    }

    [Test]
    public async Task ChunksOfADocumentThatWasRemovedAreDeletedWithoutCallingTheModel()
    {
        var repo = new InMemoryKnowledgeRepository();
        var model = new CountingEmbeddingGenerator();
        var logic = LogicFor(repo, model);
        await logic.IngestAsync([Doc("a", ("One", "alpha")), Doc("b", ("Two", "beta"))]);

        var summary = await logic.IngestAsync([Doc("a", ("One", "alpha"))]);

        await Assert.That(summary).IsEqualTo(new IngestionSummary(0, Removed: 1, Unchanged: 1));
        await Assert.That(model.Calls.Count).IsEqualTo(1);
        await Assert.That(repo.Stored.Select(s => s.Chunk.DocumentId)).IsEquivalentTo(["a"]);
    }

    [Test]
    public async Task EmbeddingCallsAskForExactlyTheColumnWidth()
    {
        var model = new CountingEmbeddingGenerator();

        await LogicFor(new InMemoryKnowledgeRepository(), model).IngestAsync([Doc("a", ("One", "alpha"))]);

        await Assert.That(model.OptionsSeen.Single()!.Dimensions).IsEqualTo(KnowledgeChunk.EmbeddingDimensions);
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(-5, 1)]
    [Arguments(3, 3)]
    [Arguments(100, IKnowledgeLogic.MaxTop)]
    public async Task SearchClampsTheRequestedCount(int requested, int expectedTop)
    {
        var repo = new InMemoryKnowledgeRepository();

        await LogicFor(repo, new CountingEmbeddingGenerator()).SearchAsync("anything", requested);

        await Assert.That(repo.LastSearchTop).IsEqualTo(expectedTop);
    }

    [Test]
    public async Task SearchMapsHitsAndRoundsTheScore()
    {
        var repo = new InMemoryKnowledgeRepository
        {
            HitsToReturn = [new KnowledgeHit("doc-1", "Doc One", "Section", "Body text.", 0.123456)]
        };

        var results = await LogicFor(repo, new CountingEmbeddingGenerator()).SearchAsync("q", 4);

        var hit = results.Single();
        await Assert.That(hit.DocumentId).IsEqualTo("doc-1");
        await Assert.That(hit.DocumentTitle).IsEqualTo("Doc One");
        await Assert.That(hit.Heading).IsEqualTo("Section");
        await Assert.That(hit.Content).IsEqualTo("Body text.");
        await Assert.That(hit.Score).IsEqualTo(0.1235);
    }

    [Test]
    public async Task ASearchFailureInTheModelBecomesKnowledgeUnavailable()
    {
        var model = new CountingEmbeddingGenerator { ThrowOnCall = new HttpRequestException("no network") };
        var logic = LogicFor(new InMemoryKnowledgeRepository(), model);

        await Assert.That(async () => await logic.SearchAsync("q", 4))
            .Throws<KnowledgeUnavailableException>();
    }

    [Test]
    public async Task AnIngestFailureInTheModelBecomesKnowledgeUnavailableAndStoresNothing()
    {
        var repo = new InMemoryKnowledgeRepository();
        var model = new CountingEmbeddingGenerator { ThrowOnCall = new InvalidOperationException("no key") };
        var logic = LogicFor(repo, model);

        await Assert.That(async () => await logic.IngestAsync([Doc("a", ("One", "alpha"))]))
            .Throws<KnowledgeUnavailableException>();
        await Assert.That(repo.Stored).IsEmpty();
    }

    [Test]
    public async Task EmbeddingsOfTheWrongSizeAreRejected()
    {
        var model = new CountingEmbeddingGenerator { Dimensions = 8 };
        var logic = LogicFor(new InMemoryKnowledgeRepository(), model);

        await Assert.That(async () => await logic.SearchAsync("q", 4))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("1536");
    }
}
