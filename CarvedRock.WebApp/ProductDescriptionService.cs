using System.Net;
using System.Text.Json;

namespace CarvedRock.WebApp;

/// <summary>What the admin form sends: the facts the draft should be written from.</summary>
public record DescribeDraftRequest(string? Name, string? Category, List<string>? KeyFeatures);

/// <param name="Success">Whether a draft came back (it may still break a rule, see <paramref name="Messages"/>).</param>
/// <param name="Description">The draft exactly as the model wrote it; never trimmed here.</param>
/// <param name="Messages">Text that is safe to show the admin: rule violations, field errors or a failure.</param>
public record DescribeDraftResult(bool Success, string? Description, List<string> Messages);

public interface IProductDescriptionService
{
    Task<DescribeDraftResult> DraftAsync(DescribeDraftRequest request, CancellationToken cancellationToken);
}

/// <summary>Calls the agent's admin-only <c>POST /agent/describe</c> with the signed-in admin's token.</summary>
public class ProductDescriptionService(IHttpClientFactory httpClientFactory) : IProductDescriptionService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DescribeDraftResult> DraftAsync(DescribeDraftRequest request, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("AI"); // adds the user's access token

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("agent/describe", request, Json, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return Failed("The description service could not be reached. Please try again.");
        }

        switch (response.StatusCode)
        {
            case HttpStatusCode.OK:
                var body = await response.Content.ReadFromJsonAsync<AgentDescribeResponse>(Json, cancellationToken);
                if (body?.Draft is null)
                    return Failed("The description service returned an unexpected reply.");
                return new DescribeDraftResult(true, body.Draft.Description, body.Violations ?? []);

            case HttpStatusCode.BadRequest: // the request itself was invalid: say which fields
                var problem = await response.Content.ReadFromJsonAsync<AgentValidationProblem>(Json, cancellationToken);
                var messages = problem?.Errors?.Values.SelectMany(v => v).ToList() ?? [];
                return Failed(messages.Count > 0 ? messages : ["That request wasn't valid."]);

            case HttpStatusCode.Unauthorized:
            case HttpStatusCode.Forbidden:
                return Failed("You are not allowed to generate descriptions.");

            case HttpStatusCode.BadGateway: // the model failed or didn't return a usable draft
                return Failed("A description could not be drafted right now. Please try again.");

            default:
                return Failed("Something went wrong drafting the description.");
        }
    }

    private static DescribeDraftResult Failed(params string[] messages) => new(false, null, [.. messages]);
    private static DescribeDraftResult Failed(List<string> messages) => new(false, null, messages);

    private record AgentDraft(string Description);
    private record AgentDescribeResponse(AgentDraft? Draft, bool IsValid, List<string>? Violations);
    private record AgentValidationProblem(Dictionary<string, string[]>? Errors);
}
