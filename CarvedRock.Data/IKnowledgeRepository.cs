namespace CarvedRock.Data;

/// <summary>Identifies a stored chunk without loading its (large) embedding.</summary>
public record KnowledgeChunkKey(int Id, string DocumentId, string ContentHash);

/// <summary>A chunk to store; the embedding is a plain float memory so callers don't need pgvector types.</summary>
public record NewKnowledgeChunk(string DocumentId, string DocumentTitle, string Heading,
    string Content, string ContentHash, ReadOnlyMemory<float> Embedding);

/// <param name="Score">Cosine similarity to the query (1 = identical direction, 0 = unrelated).</param>
public record KnowledgeHit(string DocumentId, string DocumentTitle, string Heading,
    string Content, double Score);

public interface IKnowledgeRepository
{
    Task<List<KnowledgeChunkKey>> GetChunkKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes and adds chunks in a single save (one transaction).</summary>
    Task ReplaceChunksAsync(IReadOnlyCollection<int> idsToRemove,
        IReadOnlyCollection<NewKnowledgeChunk> chunksToAdd,
        CancellationToken cancellationToken = default);

    /// <summary>Nearest chunks to the query embedding by cosine distance, best first.</summary>
    Task<List<KnowledgeHit>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int top,
        CancellationToken cancellationToken = default);
}
