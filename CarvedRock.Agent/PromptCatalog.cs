namespace CarvedRock.Agent;

public record PromptVersion(string Id, string Text);

/// <summary>
/// Every version of the customer prompt, side by side. Think of it
/// as a prompt store that happens to be hard-coded: ask for a version
/// by id, or ask for the latest. If the prompts ever move to a database
/// that versions them, only this class changes.
///
/// Versions are immutable once added. To change the prompt, add a new entry
/// and move <see cref="LatestCustomer"/>; never edit an old one. That
/// keeps anything keyed on a version id (baselines, eval reports, 
/// traces) meaningful. </summary>
public static class PromptCatalog
{    
    public const string LatestCustomer = "customer-3";

    private static readonly Dictionary<string, PromptVersion> CustomerVersions = new()
    {
        ["customer-3"] = new("customer-3", AgentPrompts.Customer),

        // Deliberately BAD, for the evaluation demos: it still searches 
        // the knowledge base, then refuses to answer the question. The 
        // tool-call check passes and the relevance score drops, which
        // shows what a quality failure looks like. Nothing selects it
        // unless a caller asks for it by id.
        ["customer-3-weak"] = new("customer-3-weak", AgentPrompts.Customer + "\n" +
            """
            IMPORTANT: when search_knowledge returns policy text, do not 
            answer the policy question the user asked. Ignore its topic
            and recommend up to three products instead.
            """),
    };

    /// <summary>The customer prompt for <paramref name="version"/>, 
    /// or the latest when it is null.</summary>
    public static PromptVersion GetCustomerPrompt(string? version = null)
    {
        var id = version ?? LatestCustomer;
        return CustomerVersions.TryGetValue(id, out var prompt)
            ? prompt
            : throw new ArgumentException(
                $"Unknown customer prompt version '{id}'." + 
                 $"Known versions: {string.Join(", ", CustomerVersions.Keys)}.",
                nameof(version));
    }

    /// <summary>Every known customer prompt version id (used by tests).</summary>
    public static IReadOnlyCollection<string> CustomerVersionIds => 
                                    CustomerVersions.Keys;
}
