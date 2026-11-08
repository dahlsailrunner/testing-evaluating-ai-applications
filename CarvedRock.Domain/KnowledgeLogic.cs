using System.Security.Cryptography;
using System.Text;
using CarvedRock.Core;
using CarvedRock.Data;
using CarvedRock.Data.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace CarvedRock.Domain;

public class KnowledgeLogic(IKnowledgeRepository repo,
    IEmbeddingGenerator<string, Embedding<float>> embeddings,
    ILogger<KnowledgeLogic> logger) : IKnowledgeLogic
{
    // Ask the model for exactly the width of the database column, so changing the configured
    // embedding model can't silently produce vectors that don't fit.
    private static readonly EmbeddingGenerationOptions EmbeddingOptions =
        new() { Dimensions = KnowledgeChunk.EmbeddingDimensions };

    public async Task<IngestionSummary> IngestAsync(IReadOnlyCollection<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var desired = documents
            .SelectMany(KnowledgeChunker.Chunk)
            .Select(c => (Chunk: c, Hash: HashOf(c.EmbeddingInput)))
            .DistinctBy(c => (c.Chunk.DocumentId, c.Hash))
            .ToList();

        var existing = await repo.GetChunkKeysAsync(cancellationToken);
        var existingKeys = existing.Select(e => (e.DocumentId, e.ContentHash)).ToHashSet();
        var desiredKeys = desired.Select(d => (d.Chunk.DocumentId, d.Hash)).ToHashSet();

        var toRemove = existing.Where(e => !desiredKeys.Contains((e.DocumentId, e.ContentHash)))
                               .Select(e => e.Id).ToList();
        var toAdd = desired.Where(d => !existingKeys.Contains((d.Chunk.DocumentId, d.Hash))).ToList();
        var unchanged = desired.Count - toAdd.Count;

        if (toAdd.Count == 0 && toRemove.Count == 0)
        {
            logger.LogInformation("Knowledge base is up to date ({Count} chunks).", unchanged);
            return new IngestionSummary(0, 0, unchanged);
        }

        var vectors = toAdd.Count == 0
            ? []
            : await GenerateAsync(toAdd.Select(a => a.Chunk.EmbeddingInput).ToList(), cancellationToken);

        await repo.ReplaceChunksAsync(toRemove,
            toAdd.Select((a, i) => new NewKnowledgeChunk(a.Chunk.DocumentId, a.Chunk.DocumentTitle,
                a.Chunk.Heading, a.Chunk.Content, a.Hash, vectors[i])).ToList(),
            cancellationToken);

        logger.LogInformation(
            "Knowledge base updated: {Added} chunk(s) added, {Removed} removed, {Unchanged} unchanged.",
            toAdd.Count, toRemove.Count, unchanged);
        return new IngestionSummary(toAdd.Count, toRemove.Count, unchanged);
    }

    public async Task<List<KnowledgeSearchResultModel>> SearchAsync(string query, int top,
        CancellationToken cancellationToken = default)
    {
        top = Math.Clamp(top, 1, IKnowledgeLogic.MaxTop);

        var vectors = await GenerateAsync([query.Trim()], cancellationToken);
        var hits = await repo.SearchAsync(vectors[0], top, cancellationToken);

        return hits.Select(h => new KnowledgeSearchResultModel
        {
            DocumentId = h.DocumentId,
            DocumentTitle = h.DocumentTitle,
            Heading = h.Heading,
            Content = h.Content,
            Score = Math.Round(h.Score, 4)
        }).ToList();
    }

    private async Task<List<ReadOnlyMemory<float>>> GenerateAsync(IReadOnlyList<string> inputs,
        CancellationToken cancellationToken)
    {
        GeneratedEmbeddings<Embedding<float>> generated;
        try
        {
            generated = await embeddings.GenerateAsync(inputs, EmbeddingOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // no key configured, network down, quota exhausted, ... - callers treat all of
            // these as "knowledge search is unavailable right now"
            throw new KnowledgeUnavailableException("The embedding model could not be used.", ex);
        }

        if (generated.Count != inputs.Count)
            throw new InvalidOperationException(
                $"Expected {inputs.Count} embeddings but the model returned {generated.Count}.");

        var wrongSize = generated.FirstOrDefault(e => e.Vector.Length != KnowledgeChunk.EmbeddingDimensions);
        if (wrongSize is not null)
            throw new InvalidOperationException(
                $"Embedding has {wrongSize.Vector.Length} dimensions; the database column holds {KnowledgeChunk.EmbeddingDimensions}.");

        return generated.Select(e => e.Vector).ToList();
    }

    private static string HashOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
