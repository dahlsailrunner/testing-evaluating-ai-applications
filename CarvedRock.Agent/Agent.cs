using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Chat;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;

namespace CarvedRock.Agent;

public class Agent(IChatClient chatClient,
            IConfiguration config,
            ILogger<Agent> logger,
            IHttpContextAccessor httpCtxAccessor)
{
    public async IAsyncEnumerable<string> GetAgentResponse(string message,
        List<ChatTurn>? history,
        [EnumeratorCancellation] CancellationToken cxl)
    {
        logger.LogInformation("Got into the Agent method.");

        var mcpClient = await McpClientHelper.GetMcpClient(config, httpCtxAccessor, cxl);

        var tools = await mcpClient.ListToolsAsync(cancellationToken: cxl);

        var prompt = await GetPromptAsync(message, mcpClient,
            httpCtxAccessor.HttpContext?.User, cxl);

        var agent = chatClient.AsAIAgent(
            instructions: prompt,
            name: "CarvedRock Assistant",
            tools: [.. tools]);

        // the agent/MCP server are stateless, so the caller (WebApp) re-sends prior turns
        // on every request - this is what lets a follow-up like "yes, add it to cart" resolve
        // against what was actually recommended earlier in the conversation.
        var messages = (history ?? [])
            .Select(turn => new Microsoft.Extensions.AI.ChatMessage(
                turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant : ChatRole.User,
                turn.Content))
            .Append(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, message))
            .ToList();

        await foreach (var update in
                    agent.RunStreamingAsync(messages, cancellationToken: cxl))
        {
            yield return update.ToString();
        }
    }

    private static async Task<string> GetPromptAsync(string message, McpClient mcpClient,
                                    ClaimsPrincipal? user, CancellationToken cxl)
    {
        if (message.StartsWith("/admin", StringComparison.InvariantCultureIgnoreCase) &&
            (user?.IsInRole("admin") ?? false))
        {
            var prompt = await mcpClient.GetPromptAsync("admin_prompt", cancellationToken: cxl);
            var adminPrompt = new StringBuilder();
            foreach (var msg in prompt.Messages)
            {
                adminPrompt.AppendLine((msg.Content as TextContentBlock)!.Text);
            }
            return adminPrompt.ToString();
        }

        return
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
}