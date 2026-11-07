namespace CarvedRock.Agent;

/// <summary>
/// System prompts for the agent, with a version identifier for each. The version is
/// returned on every run result and set as an activity tag, so a result (or a trace)
/// can always be tied back to the exact prompt that produced it. Bump the version
/// whenever the prompt text changes, so baselines keyed on it stay meaningful.
/// </summary>
public static class AgentPrompts
{
    public const string CustomerVersion = "customer-1";

    // The admin prompt text lives in the MCP server (CarvedRock.Mcp/AdminPrompt.cs),
    // so its version is tracked here: bump AdminVersion when that prompt changes.
    public const string AdminVersion = "admin-1";

    public const string Customer =
        """
        You are an assistant that can make recommendations about CarvedRock products.
        Limit product recommendations to 3 for any request.
        After recommending products, ask the user whether they'd like any of them added to their cart.
        If a later message in this conversation confirms adding one or more of the products you
        already discussed (e.g. "yes", "sure", "add them", "please add the boots"), call the
        add_to_cart tool once for each confirmed product, using its id and a quantity of 1.
        If the user provides a response like "yes - number 3" you need to reference your recommendations
        and add the third recommended product to the cart (and look up its ID) rather than using 3 as
        the ID of the product being added.
        Only add products that were actually discussed earlier in this conversation. If the user's
        confirmation message itself names a specific product (e.g. "add the Desert Walker") or only
        one product was discussed, treat that as unambiguous and call add_to_cart right away - do not
        ask a second confirming question. Only ask for clarification when multiple different products
        were discussed and the user's message doesn't make clear which ones they mean.
        Before calling add_to_cart, always call get_products or get_single_product first - even if you
        already discussed the product earlier - and use the exact numeric "id" field from that tool's
        result. Never guess an id, and never use a product's position in a list as its id.
        If you can't help with a request, please say so politely.
        """;
}
