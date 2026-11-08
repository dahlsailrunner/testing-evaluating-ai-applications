using System.Net;
using System.Net.Http.Headers;

namespace CarvedRock.AppTests;

// POST /agent/describe (the generative, non-agentic feature): who may call it, and what it
// rejects before the model is ever involved. None of these reach the model - the endpoint checks
// the caller and the request first - so they need no OpenAI key.
[ClassDataSource<AppFixture>(Shared = SharedType.PerTestSession)]
public class DescribeProductTests(AppFixture fixture)
{
    private static readonly object ValidRequest = new
    {
        name = "Alpine Trekker",
        category = "boots",
        keyFeatures = new[] { "Waterproof membrane", "Ankle support" }
    };

    private async Task<HttpClient> AgentClientAsync(string? clientId)
    {
        var client = fixture.CreateHttpClient("agent");
        if (clientId is not null)
        {
            var token = await AppFixture.GetClientCredsAccessTokenAsync(clientId, "secret");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return client;
    }

    [Test]
    public async Task AnonymousCallersAreRejected()
    {
        var client = await AgentClientAsync(null);

        var response = await client.PostAsJsonAsync("/agent/describe", ValidRequest);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task SignedInNonAdminsAreForbidden()
    {
        var client = await AgentClientAsync("m2m"); // customer

        var response = await client.PostAsJsonAsync("/agent/describe", ValidRequest);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AdminsGetFieldLevelErrorsForAnInvalidRequest()
    {
        var client = await AgentClientAsync("m2m.short"); // admin

        var response = await client.PostAsJsonAsync("/agent/describe", new
        {
            name = "",
            category = "tents",
            keyFeatures = Array.Empty<string>()
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemBody>();
        await Assert.That(problem!.Errors.Keys).Contains("Name");
        await Assert.That(problem.Errors.Keys).Contains("Category");
        await Assert.That(problem.Errors.Keys).Contains("KeyFeatures");
    }

    [Test]
    public async Task AnOverlongNameIsRejectedBeforeAnythingIsGenerated()
    {
        var client = await AgentClientAsync("m2m.short");

        var response = await client.PostAsJsonAsync("/agent/describe", new
        {
            name = new string('x', 51),
            category = "boots",
            keyFeatures = new[] { "Waterproof" }
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemBody>();
        await Assert.That(problem!.Errors["Name"]).Contains("Name must not exceed 50 characters.");
    }

    [Test]
    public async Task ABodyWithNoFieldsAtAllIsABadRequest()
    {
        var client = await AgentClientAsync("m2m.short");

        var response = await client.PostAsJsonAsync("/agent/describe", new { });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}

public record ValidationProblemBody(Dictionary<string, string[]> Errors);
