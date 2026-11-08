using CarvedRock.Api.Controllers;
using CarvedRock.Core;
using CarvedRock.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace CarvedRock.ApiTests;

// The real corpus, ingested (in TestData) with deterministic fake embeddings into a real
// pgvector database. These prove the search plumbing; semantic quality of the real embedding
// model is covered by the AppTests and is the subject of retrieval evaluation.
public class KnowledgeApiTests : ApiTestsBase
{
    private static async Task<KnowledgeSearchResponse> SearchAsync(HttpClient client, string query, int? top = null)
    {
        var url = $"/knowledge/search?q={Uri.EscapeDataString(query)}" + (top is null ? "" : $"&top={top}");
        var response = await client.GetAsync(url);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<KnowledgeSearchResponse>())!;
    }

    [Test]
    [Arguments("How many days do I have to return unworn boots for a refund?", "returns-and-exchanges")]
    [Arguments("What temperature rating should I choose for a sleeping bag?", "sleeping-bag-temperature-ratings")]
    [Arguments("How do I waterproof my boots again?", "boot-care-and-waterproofing")]
    [Arguments("What length kayak is best for beginners on a lake?", "kayak-sizing-and-transport")]
    [Arguments("Does shipping cost extra for kayaks sent by freight?", "shipping-and-delivery")]
    [Arguments("How do I cancel my order?", "order-changes-and-cancellation")]
    [Arguments("What is a base layer and should I wear merino?", "layering-guide")]
    [Arguments("How do I stop condensation and seal the seams on my tent?", "tent-care-and-setup")]
    [Arguments("How many years of warranty cover a kayak hull?", "warranty")]
    [Arguments("Can I combine a promo code with another offer?", "promotions-and-discounts")]
    [Arguments("How should I measure my feet and pick a half size?", "boot-fit-and-sizing")]
    public async Task SearchRanksTheExpectedDocumentInTheTopThree(string question, string expectedDocumentId)
    {
        var client = Factory.CreateClient(); // no sign-in: the documents are public

        var response = await SearchAsync(client, question);

        await Assert.That(response.Results.Take(3).Select(r => r.DocumentId)).Contains(expectedDocumentId);
    }

    [Test]
    public async Task ResultsAreBestFirstAndCarryEverythingTheCallerNeeds()
    {
        var response = await SearchAsync(Factory.CreateClient(), "return policy for boots", top: 5);

        await Assert.That(response.Query).IsEqualTo("return policy for boots");
        await Assert.That(response.Results.Count).IsEqualTo(5);

        var scores = response.Results.Select(r => r.Score).ToList();
        await Assert.That(scores).IsEquivalentTo(scores.OrderByDescending(s => s));

        foreach (var result in response.Results)
        {
            await Assert.That(result.DocumentId).IsNotEmpty();
            await Assert.That(result.DocumentTitle).IsNotEmpty();
            await Assert.That(result.Heading).IsNotEmpty();
            await Assert.That(result.Content).IsNotEmpty();
            await Assert.That(result.Score).IsBetween(0, 1);
        }
    }

    [Test]
    [Arguments(null, 4)]  // default
    [Arguments(2, 2)]
    [Arguments(0, 1)]     // clamped up
    [Arguments(100, 10)]  // clamped down
    public async Task TheNumberOfResultsIsClamped(int? requested, int expected)
    {
        var response = await SearchAsync(Factory.CreateClient(), "shipping", requested);

        await Assert.That(response.Results.Count).IsEqualTo(expected);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public async Task ABlankQueryIsABadRequest(string query)
    {
        var response = await Factory.CreateClient()
            .GetAsync($"/knowledge/search?q={Uri.EscapeDataString(query)}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AMissingQueryIsABadRequest()
    {
        var response = await Factory.CreateClient().GetAsync("/knowledge/search");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AnOverlongQueryIsABadRequest()
    {
        var response = await Factory.CreateClient()
            .GetAsync($"/knowledge/search?q={new string('x', 501)}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task WhenTheEmbeddingModelIsUnavailableSearchAnswers503()
    {
        // Controller-level on purpose: the per-test hosts share one configuration, so the simplest
        // way to make the model "fail" is to hand the controller a logic that says it did.
        var controller = new KnowledgeController(new UnavailableKnowledgeLogic())
        {
            ProblemDetailsFactory = new MinimalProblemDetailsFactory(),
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.Search("returns");

        var objectResult = (ObjectResult)result;
        await Assert.That(objectResult.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(((ProblemDetails)objectResult.Value!).Title)
            .IsEqualTo("Knowledge search is unavailable");
    }

    private class UnavailableKnowledgeLogic : IKnowledgeLogic
    {
        public Task<IngestionSummary> IngestAsync(IReadOnlyCollection<KnowledgeDocument> documents,
            CancellationToken cancellationToken = default) => throw new KnowledgeUnavailableException("simulated");

        public Task<List<KnowledgeSearchResultModel>> SearchAsync(string query, int top,
            CancellationToken cancellationToken = default) => throw new KnowledgeUnavailableException("simulated");
    }

    private class MinimalProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(HttpContext httpContext, int? statusCode = null,
            string? title = null, string? type = null, string? detail = null, string? instance = null) =>
            new() { Status = statusCode, Title = title, Type = type, Detail = detail, Instance = instance };

        public override ValidationProblemDetails CreateValidationProblemDetails(HttpContext httpContext,
            Microsoft.AspNetCore.Mvc.ModelBinding.ModelStateDictionary modelStateDictionary,
            int? statusCode = null, string? title = null, string? type = null, string? detail = null,
            string? instance = null) => new(modelStateDictionary) { Status = statusCode, Title = title };
    }
}

// defined here to document the JSON contract callers (the MCP server) depend on
public record KnowledgeSearchResponse(string Query, List<KnowledgeResult> Results);
public record KnowledgeResult(string DocumentId, string DocumentTitle, string Heading, string Content, double Score);
