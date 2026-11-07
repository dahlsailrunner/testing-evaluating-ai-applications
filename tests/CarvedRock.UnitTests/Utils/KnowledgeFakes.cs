using CarvedRock.Data;
using CarvedRock.Data.Entities;
using Microsoft.Extensions.AI;

namespace CarvedRock.UnitTests.Utils;

/// <summary>
/// Embedding generator that never calls a model: every text gets a fixed-width vector derived
/// from its length. It records what it was asked, so tests can assert "the model was (not) called".
/// </summary>
public class CountingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<IReadOnlyList<string>> Calls { get; } = [];
    public List<EmbeddingGenerationOptions?> OptionsSeen { get; } = [];
    public int Dimensions { get; set; } = KnowledgeChunk.EmbeddingDimensions;
    public Exception? ThrowOnCall { get; set; }

    public int TotalInputs => Calls.Sum(c => c.Count);

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (ThrowOnCall is not null) throw ThrowOnCall;

        var inputs = values.ToList();
        Calls.Add(inputs);
        OptionsSeen.Add(options);

        var vectors = inputs.Select(text =>
        {
            var v = new float[Dimensions];
            v[text.Length % Dimensions] = 1f;
            return new Embedding<float>(v);
        });
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(vectors));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

/// <summary>In-memory stand-in for the database-backed knowledge repository.</summary>
public class InMemoryKnowledgeRepository : IKnowledgeRepository
{
    private int _nextId = 1;
    public List<(int Id, NewKnowledgeChunk Chunk)> Stored { get; } = [];

    /// <summary>What <see cref="SearchAsync"/> returns, regardless of the query.</summary>
    public List<KnowledgeHit> HitsToReturn { get; set; } = [];
    public int LastSearchTop { get; private set; }

    public Task<List<KnowledgeChunkKey>> GetChunkKeysAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Stored.Select(s => new KnowledgeChunkKey(s.Id, s.Chunk.DocumentId, s.Chunk.ContentHash)).ToList());

    public Task ReplaceChunksAsync(IReadOnlyCollection<int> idsToRemove,
        IReadOnlyCollection<NewKnowledgeChunk> chunksToAdd, CancellationToken cancellationToken = default)
    {
        Stored.RemoveAll(s => idsToRemove.Contains(s.Id));
        Stored.AddRange(chunksToAdd.Select(c => (_nextId++, c)));
        return Task.CompletedTask;
    }

    public Task<List<KnowledgeHit>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int top,
        CancellationToken cancellationToken = default)
    {
        LastSearchTop = top;
        return Task.FromResult(HitsToReturn.Take(top).ToList());
    }
}
