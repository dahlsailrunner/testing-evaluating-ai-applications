using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;

namespace CarvedRock.Agent;

public class Agent(IChatClient chatClient,
            IMcpClientProvider mcpClientProvider,
            ILogger<Agent> logger,
            IHttpContextAccessor httpCtxAccessor)
{
    /// <summary>Streaming run used by the web app's chat. Yields text only.</summary>
    public async IAsyncEnumerable<string> GetAgentResponse(string message,
        List<ChatTurn>? history,
        [EnumeratorCancellation] CancellationToken cxl)
    {
        logger.LogInformation("Got into the Agent method.");

        var (agent, messages, _) = await PrepareAsync(message, history, cxl);

        await foreach (var update in
                    agent.RunStreamingAsync(messages, cancellationToken: cxl))
        {
            yield return update.ToString();
        }
    }

    /// <summary>
    /// Non-streaming run that returns everything about the run (tool calls, usage,
    /// timing, prompt version) rather than just the text.
    /// </summary>
    public async Task<AgentRunResult> RunAsync(string message,
        List<ChatTurn>? history,
        CancellationToken cxl)
    {
        logger.LogInformation("Got into the Agent run method.");

        var (agent, messages, promptVersion) = await PrepareAsync(message, history, cxl);

        var stopwatch = Stopwatch.StartNew();
        var response = await agent.RunAsync(messages, cancellationToken: cxl);
        stopwatch.Stop();

        return new AgentRunResult(response.Text, messages, [.. response.Messages],
            response.Usage, stopwatch.Elapsed, promptVersion);
    }

    // Shared by both run paths so they always use the same tools, prompt, and history handling.
    private async Task<(AIAgent Agent, List<ChatMessage> Messages, string PromptVersion)> PrepareAsync(
        string message, List<ChatTurn>? history, CancellationToken cxl)
    {
        var mcpClient = await mcpClientProvider.GetClientAsync(cxl);

        var tools = await mcpClient.ListToolsAsync(cancellationToken: cxl);

        var (prompt, promptVersion) = await GetPromptAsync(message, mcpClient,
            httpCtxAccessor.HttpContext?.User, cxl);
        Activity.Current?.SetTag("carvedrock.prompt.version", promptVersion);

        var agent = chatClient.AsAIAgent(
            instructions: prompt,
            name: "CarvedRock Assistant",
            tools: [.. tools]);

        // the agent/MCP server are stateless, so the caller (WebApp) re-sends prior turns
        // on every request - this is what lets a follow-up like "yes, add it to cart" resolve
        // against what was actually recommended earlier in the conversation.
        var messages = (history ?? [])
            .Select(turn => new ChatMessage(
                turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase) ? ChatRole.Assistant : ChatRole.User,
                turn.Content))
            .Append(new ChatMessage(ChatRole.User, message))
            .ToList();

        return (agent, messages, promptVersion);
    }

    private static async Task<(string Prompt, string Version)> GetPromptAsync(string message,
                                    McpClient mcpClient, ClaimsPrincipal? user, CancellationToken cxl)
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
            return (adminPrompt.ToString(), AgentPrompts.AdminVersion);
        }

        return (AgentPrompts.Customer, AgentPrompts.CustomerVersion);
    }
}
