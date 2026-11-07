using CarvedRock.Core;

namespace CarvedRock.Domain;

/// <param name="Added">Chunks that were embedded and stored.</param>
/// <param name="Removed">Stored chunks that no longer match any document.</param>
/// <param name="Unchanged">Chunks that were already stored and were not re-embedded.</param>
public record IngestionSummary(int Added, int Removed, int Unchanged);

/// <summary>Raised when the embedding model can't be reached or isn't configured.</summary>
public class KnowledgeUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public interface IKnowledgeLogic
{
    public const int DefaultTop = 4;
    public const int MaxTop = 10;
    public const int MaxQueryLength = 500;

    /// <summary>Embeds and stores whatever changed in the documents; unchanged chunks are skipped.</summary>
    Task<IngestionSummary> IngestAsync(IReadOnlyCollection<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>Top matching chunks for the query, best first. <paramref name="top"/> is clamped to 1..MaxTop.</summary>
    /// <exception cref="KnowledgeUnavailableException">The embedding model could not be used.</exception>
    Task<List<KnowledgeSearchResultModel>> SearchAsync(string query, int top,
        CancellationToken cancellationToken = default);
}
