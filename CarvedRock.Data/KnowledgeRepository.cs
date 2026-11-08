using CarvedRock.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace CarvedRock.Data;

public class KnowledgeRepository(LocalContext ctx) : IKnowledgeRepository
{
    public async Task<List<KnowledgeChunkKey>> GetChunkKeysAsync(CancellationToken cancellationToken = default)
    {
        // projected: never pull the embedding column back just to compare hashes
        return await ctx.KnowledgeChunks.AsNoTracking()
            .Select(c => new KnowledgeChunkKey(c.Id, c.DocumentId, c.ContentHash))
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceChunksAsync(IReadOnlyCollection<int> idsToRemove,
        IReadOnlyCollection<NewKnowledgeChunk> chunksToAdd, CancellationToken cancellationToken = default)
    {
        // Deleting by key stub and inserting happen in one SaveChanges, i.e. one transaction. (An
        // explicit transaction would clash with the retrying execution strategy Aspire configures.)
        ctx.KnowledgeChunks.RemoveRange(idsToRemove.Select(id => new KnowledgeChunk { Id = id }));

        ctx.KnowledgeChunks.AddRange(chunksToAdd.Select(c => new KnowledgeChunk
        {
            DocumentId = c.DocumentId,
            DocumentTitle = c.DocumentTitle,
            Heading = c.Heading,
            Content = c.Content,
            ContentHash = c.ContentHash,
            Embedding = new Vector(c.Embedding)
        }));

        await ctx.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<KnowledgeHit>> SearchAsync(ReadOnlyMemory<float> queryEmbedding, int top,
        CancellationToken cancellationToken = default)
    {
        var query = new Vector(queryEmbedding);

        // exact (not approximate) nearest-neighbour search: the corpus is tiny, and exact results
        // keep retrieval behavior reproducible
        return await ctx.KnowledgeChunks.AsNoTracking()
            .OrderBy(c => c.Embedding.CosineDistance(query))
            .Take(top)
            .Select(c => new KnowledgeHit(c.DocumentId, c.DocumentTitle, c.Heading, c.Content,
                1 - c.Embedding.CosineDistance(query)))
            .ToListAsync(cancellationToken);
    }
}
