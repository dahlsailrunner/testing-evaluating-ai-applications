using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CarvedRock.AppTests;

// The knowledge base end to end: pgvector in the real Postgres container, the real OpenAI
// embedding model (so, like the chat tests, these need an OpenAI key), the API endpoint and the
// MCP tool. Retrieval *quality* is only spot-checked here with clear-cut paraphrased questions;
// measuring it properly is what retrieval evaluation is for.
[ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
public class KnowledgeTests(AppFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // The API embeds the documents in the background right after it starts, so the first search
    // can find nothing (or answer 503) for a few seconds. Ingestion is a single transaction, so
    // "has results" really does mean "fully ingested".
    private async Task WaitForKnowledgeBaseAsync()
    {
        var client = fixture.CreateHttpClient("api");
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var response = await client.GetAsync("/knowledge/search?q=returns&top=1");
            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<ApiKnowledgeResponse>(Json);
                if (body!.Results.Count > 0) return;
            }
            await Task.Delay(2_000);
        }
        throw new Exception("The knowledge base was not ingested within 2 minutes. Is the OpenAI key configured?");
    }

    private static async Task<T> CallToolAsync<T>(McpClient client, string tool, Dictionary<string, object?> args)
    {
        var response = await client.CallToolAsync(tool, args);
        var text = (TextContentBlock)response.Content.First(c => c.Type == "text");
        return JsonSerializer.Deserialize<T>(text.Text, Json)!;
    }

    [Test]
    public async Task SearchEndpointRanksTheCurrentReturnsPolicyNearTheTop()
    {
        await WaitForKnowledgeBaseAsync();
        var client = fixture.CreateHttpClient("api"); // anonymous: the documents are public

        var response = await client.GetFromJsonAsync<ApiKnowledgeResponse>(
            "/knowledge/search?q=" + Uri.EscapeDataString("How long do I have to return unworn boots?"), Json);

        await Assert.That(response!.Results.Count).IsEqualTo(4); // the default
        await Assert.That(response.Results.Take(3).Select(r => r.DocumentId)).Contains("returns-and-exchanges");
        var scores = response.Results.Select(r => r.Score).ToList();
        await Assert.That(scores).IsEquivalentTo(scores.OrderByDescending(s => s));
        await Assert.That(scores.All(s => s is > 0 and <= 1)).IsTrue();
    }

    [Test]
    [Arguments("Can I send my boots back if I only tried them on the carpet?", "returns-and-exchanges")]
    [Arguments("Which sleeping bag rating keeps me warm on a freezing night?", "sleeping-bag-temperature-ratings")]
    [Arguments("My boots leak now, how do I make them water resistant again?", "boot-care-and-waterproofing")]
    [Arguments("I ordered the wrong thing and it hasn't shipped yet, what can I do?", "order-changes-and-cancellation")]
    [Arguments("Do you deliver to Mexico?", "shipping-and-delivery")]
    [Arguments("How do I carry my kayak on my car?", "kayak-sizing-and-transport")]
    public async Task ParaphrasedQuestionsFindTheRightDocumentInTheTopThree(string question, string expectedDocumentId)
    {
        await WaitForKnowledgeBaseAsync();
        var client = fixture.CreateHttpClient("api");

        var response = await client.GetFromJsonAsync<ApiKnowledgeResponse>(
            "/knowledge/search?q=" + Uri.EscapeDataString(question) + "&top=3", Json);

        await Assert.That(response!.Results.Select(r => r.DocumentId)).Contains(expectedDocumentId);
    }

    [Test]
    public async Task SearchKnowledgeToolIsVisibleToAnonymousUsers()
    {
        var mcp = await fixture.GetAnonymousMcpClient();

        var tools = await mcp.ListToolsAsync();

        await Assert.That(tools.Any(t => t.Name == "search_knowledge")).IsTrue();
    }

    [Test]
    public async Task SearchKnowledgeToolReturnsRankedChunksExactlyAsTheApiDoes()
    {
        await WaitForKnowledgeBaseAsync();
        var mcp = await fixture.GetAnonymousMcpClient();

        var outcome = await CallToolAsync<McpKnowledgeOutcome>(mcp, "search_knowledge",
            new() { { "query", "What does the warranty cover on a kayak hull?" }, { "topK", 3 } });

        await Assert.That(outcome.Status).IsEqualTo("ok");
        await Assert.That(outcome.Results.Count).IsEqualTo(3);
        await Assert.That(outcome.Results.Select(r => r.DocumentId)).Contains("warranty");
        await Assert.That(outcome.Results.All(r =>
            r.DocumentTitle.Length > 0 && r.Heading.Length > 0 && r.Content.Length > 0)).IsTrue();
    }

    [Test]
    public async Task SearchKnowledgeToolClampsTopK()
    {
        await WaitForKnowledgeBaseAsync();
        var mcp = await fixture.GetAnonymousMcpClient();

        var outcome = await CallToolAsync<McpKnowledgeOutcome>(mcp, "search_knowledge",
            new() { { "query", "shipping" }, { "topK", 50 } });

        await Assert.That(outcome.Status).IsEqualTo("ok");
        await Assert.That(outcome.Results.Count).IsEqualTo(10);
    }

    [Test]
    public async Task SearchKnowledgeToolRejectsABlankQuery()
    {
        var mcp = await fixture.GetAnonymousMcpClient();

        var outcome = await CallToolAsync<McpKnowledgeOutcome>(mcp, "search_knowledge",
            new() { { "query", "   " } });

        await Assert.That(outcome.Status).IsEqualTo("error");
        await Assert.That(outcome.Results).IsEmpty();
    }
}

public record ApiKnowledgeResult(string DocumentId, string DocumentTitle, string Heading, string Content, double Score);
public record ApiKnowledgeResponse(string Query, List<ApiKnowledgeResult> Results);
public record McpKnowledgeOutcome(string Status, List<ApiKnowledgeResult> Results, string? Message);
