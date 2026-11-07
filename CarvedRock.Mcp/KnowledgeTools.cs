using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Net;

namespace CarvedRock.Mcp;

/// <summary>One retrieved piece of a help document, exactly as the API returns it.</summary>
public record KnowledgeChunkResult
{
    public string DocumentId { get; set; } = null!;
    public string DocumentTitle { get; set; } = null!;
    public string Heading { get; set; } = null!;
    public string Content { get; set; } = null!;
    public double Score { get; set; }
}

public record KnowledgeSearchApiResponse
{
    public string Query { get; set; } = null!;
    public List<KnowledgeChunkResult> Results { get; set; } = [];
}

/// <param name="Status"><c>ok</c> or <c>unavailable</c>.</param>
public record KnowledgeSearchOutcome(string Status, List<KnowledgeChunkResult> Results, string? Message = null);

// Read-only help documents, so anonymous callers can use it too.
[AllowAnonymous]
[McpServerToolType]
public class KnowledgeTools(IHttpClientFactory httpClientFactory)
{
    private const int MaxTopK = 10;

    [McpServerTool(Name = "search_knowledge")]
    [Description("Search CarvedRock's help documents: returns and exchanges, shipping, warranty, boot fit " +
                 "and sizing, boot care, layering, sleeping bag ratings, tent care, kayak sizing and " +
                 "transport, order changes and cancellation, and promotions. Use it for any question about " +
                 "CarvedRock policies or how to choose, use or care for gear - not for looking up products. " +
                 "Results are ranked by similarity to the query, best first. Each has the document title, " +
                 "section heading, text and a similarity score. They are the closest matches, not " +
                 "guaranteed answers, so check that they actually address the question.")]
    public async Task<KnowledgeSearchOutcome> SearchKnowledgeAsync(
        [Description("The question or topic to look up, in plain language.")] string query,
        [Description("How many results to return (1 to 10, default 4).")] int topK = 4,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new KnowledgeSearchOutcome("error", [], "query must not be empty.");

        topK = Math.Clamp(topK, 1, MaxTopK);

        var client = httpClientFactory.CreateClient("CarvedRockApi");
        var response = await client.GetAsync(
            $"/knowledge/search?q={Uri.EscapeDataString(query)}&top={topK}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
            return new KnowledgeSearchOutcome("unavailable", [],
                "Knowledge search is unavailable right now.");

        if (response.StatusCode == HttpStatusCode.BadRequest)
            return new KnowledgeSearchOutcome("error", [], "The search query was not valid (is it too long?).");

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Error searching knowledge base; HttpResponseCode was {(int)response.StatusCode}");

        var body = await response.Content.ReadFromJsonAsync<KnowledgeSearchApiResponse>(cancellationToken);
        return new KnowledgeSearchOutcome("ok", body?.Results ?? []);
    }
}
