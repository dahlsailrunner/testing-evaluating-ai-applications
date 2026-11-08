using Pgvector;

namespace CarvedRock.Data.Entities;

/// <summary>
/// One searchable piece of the knowledge base (a section of a policy or guide document)
/// together with its embedding.
/// </summary>
public class KnowledgeChunk
{
    // text-embedding-3-small's native size. The column is fixed-width, so switching to a model
    // with a different size means a new migration (and re-embedding) - the embedding calls ask
    // the model for exactly this many dimensions so a model change alone can't break inserts.
    public const int EmbeddingDimensions = 1536;

    public int Id { get; set; }

    /// <summary>Stable id of the source document (its file name without extension).</summary>
    public string DocumentId { get; set; } = null!;
    public string DocumentTitle { get; set; } = null!;
    public string Heading { get; set; } = null!;
    public string Content { get; set; } = null!;

    /// <summary>Hash of the exact text that was embedded; lets ingestion skip unchanged chunks.</summary>
    public string ContentHash { get; set; } = null!;

    /// <summary>pgvector <c>vector(1536)</c> column.</summary>
    public Vector Embedding { get; set; } = null!;
}
