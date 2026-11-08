using System.Diagnostics;
using CarvedRock.Core;
using Microsoft.Extensions.AI;

namespace CarvedRock.Agent;

public record DescribeProductRequest(string? Name, string? Category, List<string>? KeyFeatures)
{
    public const int MaxKeyFeatures = 8;
    public const int MaxKeyFeatureLength = 100;

    /// <summary>Problems with the request itself (nothing to do with the model's output), keyed by field.</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(Name))
            errors[nameof(Name)] = ["Name is required."];
        else if (Name.Length > ProductLimits.MaxNameLength)
            errors[nameof(Name)] = [$"Name must not exceed {ProductLimits.MaxNameLength} characters."];

        if (Category is null || !Constants.Categories.Contains(Category))
            errors[nameof(Category)] = [$"Category must be one of {string.Join(",", Constants.Categories)}."];

        if (KeyFeatures is null || KeyFeatures.Count == 0)
            errors[nameof(KeyFeatures)] = ["Provide at least one key feature."];
        else if (KeyFeatures.Count > MaxKeyFeatures)
            errors[nameof(KeyFeatures)] = [$"Provide no more than {MaxKeyFeatures} key features."];
        else if (KeyFeatures.Any(string.IsNullOrWhiteSpace))
            errors[nameof(KeyFeatures)] = ["Key features must not be blank."];
        else if (KeyFeatures.Any(f => f.Length > MaxKeyFeatureLength))
            errors[nameof(KeyFeatures)] = [$"Each key feature must not exceed {MaxKeyFeatureLength} characters."];

        return errors;
    }
}

/// <summary>What the model is asked to produce (its structured output).</summary>
public record ProductDraft(string Description);

/// <summary>
/// The draft exactly as the model wrote it, plus whether it would pass product validation. A draft
/// that breaks a rule is reported with its violations - never trimmed or "fixed" - so the caller
/// sees what the model really did.
/// </summary>
public record ProductDescriptionResult(
    ProductDraft Draft,
    bool IsValid,
    List<string> Violations,
    string PromptVersion,
    AgentUsage? Usage,
    double ElapsedMs);

public class ProductDescriptionUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// The draft's rules, written against the same limits (<see cref="ProductLimits"/>) and with the same
/// messages as the product validator in Domain, which the Agent can't reference without dragging in
/// the data layer. A unit test checks the two stay in step.
/// </summary>
public static class ProductDraftRules
{
    public static List<string> Check(string? description)
    {
        List<string> violations = [];

        if (string.IsNullOrWhiteSpace(description))
            violations.Add("Description is required.");
        else if (description.Length > ProductLimits.MaxDescriptionLength)
            violations.Add($"Description must not exceed {ProductLimits.MaxDescriptionLength} characters.");

        return violations;
    }
}

/// <summary>
/// Generative (non-agentic) feature: no tools, no conversation - one structured completion that
/// turns a few product facts into a catalog description.
/// </summary>
public class ProductDescriber(IChatClient chatClient, ILogger<ProductDescriber> logger)
{
    // a cost guardrail: a 150-character description needs a few dozen tokens at most
    private const int MaxOutputTokens = 300;

    public async Task<ProductDescriptionResult> DescribeAsync(DescribeProductRequest request,
        CancellationToken cancellationToken)
    {
        Activity.Current?.SetTag("carvedrock.prompt.version", AgentPrompts.DescribeVersion);

        List<ChatMessage> messages =
        [
            new(ChatRole.System, AgentPrompts.Describe),
            new(ChatRole.User, BuildUserMessage(request))
        ];

        var stopwatch = Stopwatch.StartNew();
        ChatResponse<ProductDraft> response;
        try
        {
            response = await chatClient.GetResponseAsync<ProductDraft>(messages,
                new ChatOptions { MaxOutputTokens = MaxOutputTokens }, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ProductDescriptionUnavailableException("The model could not be reached.", ex);
        }
        stopwatch.Stop();

        if (!response.TryGetResult(out var draft))
        {
            logger.LogWarning("The model's reply could not be read as a product draft: {Text}", response.Text);
            throw new ProductDescriptionUnavailableException("The model did not return a usable draft.");
        }

        var violations = ProductDraftRules.Check(draft.Description);
        var usage = response.Usage is null
            ? null
            : new AgentUsage(response.Usage.InputTokenCount, response.Usage.OutputTokenCount,
                             response.Usage.TotalTokenCount);

        return new ProductDescriptionResult(draft, violations.Count == 0, violations,
            AgentPrompts.DescribeVersion, usage, stopwatch.Elapsed.TotalMilliseconds);
    }

    private static string BuildUserMessage(DescribeProductRequest request) =>
        $"""
        Product name: {request.Name!.Trim()}
        Category: {request.Category}
        Key features:
        {string.Join("\n", request.KeyFeatures!.Select(f => $"- {f.Trim()}"))}
        """;
}
