namespace CarvedRock.Core;

/// <summary>One retrieved piece of the knowledge base.</summary>
public record KnowledgeSearchResultModel
{
    public string DocumentId { get; set; } = null!;
    public string DocumentTitle { get; set; } = null!;
    public string Heading { get; set; } = null!;
    public string Content { get; set; } = null!;

    /// <summary>Cosine similarity to the query; ranking signal only, not a guarantee of relevance.</summary>
    public double Score { get; set; }
}

public record KnowledgeSearchResponseModel
{
    public string Query { get; set; } = null!;
    public List<KnowledgeSearchResultModel> Results { get; set; } = [];
}
