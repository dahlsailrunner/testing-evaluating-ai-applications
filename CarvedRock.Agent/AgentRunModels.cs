using Microsoft.Extensions.AI;
using System.Text.Json;

namespace CarvedRock.Agent;

/// <summary>
/// In-process result of a non-streaming agent run. Unlike the streaming path (which only
/// yields text), this keeps everything needed to judge a run: the full conversation that
/// went in, every message that came back (including tool calls and tool results), token
/// usage, wall-clock time, and the prompt version.
/// </summary>
public record AgentRunResult(
    string Text,
    IReadOnlyList<ChatMessage> Input,
    IReadOnlyList<ChatMessage> Messages,
    UsageDetails? Usage,
    TimeSpan Elapsed,
    string PromptVersion);

/// <summary>JSON shape returned by <c>POST /agent/run</c>.</summary>
public record AgentRunResponse(
    string Text,
    string PromptVersion,
    double ElapsedMs,
    AgentUsage? Usage,
    List<AgentMessage> Messages,
    List<AgentToolCall> ToolCalls);

public record AgentUsage(long? InputTokens, long? OutputTokens, long? TotalTokens);

public record AgentMessage(string Role, string? Text);

/// <summary>One tool invocation, with its result paired up by call id.</summary>
public record AgentToolCall(
    string CallId,
    string Name,
    IReadOnlyDictionary<string, object?>? Arguments,
    string? Result,
    bool IsError);

public static class AgentRunMapper
{
    public static AgentRunResponse ToResponse(AgentRunResult run)
    {
        var messages = run.Messages
            .Select(m => new AgentMessage(m.Role.Value, string.IsNullOrEmpty(m.Text) ? null : m.Text))
            .ToList();

        var results = run.Messages
            .SelectMany(m => m.Contents.OfType<FunctionResultContent>())
            .GroupBy(r => r.CallId)
            .ToDictionary(g => g.Key, g => g.First());

        var toolCalls = run.Messages
            .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
            .Select(call =>
            {
                results.TryGetValue(call.CallId, out var result);
                return new AgentToolCall(
                    call.CallId,
                    call.Name,
                    call.Arguments is null ? null : new Dictionary<string, object?>(call.Arguments),
                    ResultToString(result?.Result),
                    result?.Exception is not null);
            })
            .ToList();

        var usage = run.Usage is null
            ? null
            : new AgentUsage(run.Usage.InputTokenCount, run.Usage.OutputTokenCount, run.Usage.TotalTokenCount);

        return new AgentRunResponse(run.Text, run.PromptVersion, run.Elapsed.TotalMilliseconds,
            usage, messages, toolCalls);
    }

    private static string? ResultToString(object? result) => result switch
    {
        null => null,
        string s => s,
        JsonElement je => je.GetRawText(),
        _ => JsonSerializer.Serialize(result)
    };
}
