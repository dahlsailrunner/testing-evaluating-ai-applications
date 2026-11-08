using CarvedRock.Agent;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace CarvedRock.UnitTests;

public class AgentRunMapperTests
{
    private static AgentRunResult RunWith(IEnumerable<ChatMessage> messages, UsageDetails? usage = null) =>
        new("done", [new ChatMessage(ChatRole.User, "hi")], [.. messages], usage,
            TimeSpan.FromMilliseconds(250), AgentPrompts.CustomerVersion);

    [Test]
    public async Task ToolCallsArePairedWithTheirResultsByCallId()
    {
        var result = AgentRunMapper.ToResponse(RunWith(
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", "get_single_product",
                    new Dictionary<string, object?> { ["id"] = 7 }),
                new FunctionCallContent("call-2", "add_to_cart",
                    new Dictionary<string, object?> { ["productId"] = 7, ["quantity"] = 1 })
            ]),
            // results deliberately returned in the opposite order
            new ChatMessage(ChatRole.Tool, [new FunctionResultContent("call-2", "ok")]),
            new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent("call-1", JsonDocument.Parse("""{"id":7}""").RootElement)]),
            new ChatMessage(ChatRole.Assistant, "Added it.")
        ]));

        await Assert.That(result.ToolCalls.Count).IsEqualTo(2);
        await Assert.That(result.ToolCalls[0].Name).IsEqualTo("get_single_product");
        await Assert.That(result.ToolCalls[0].Result).IsEqualTo("""{"id":7}""");
        await Assert.That(result.ToolCalls[1].Name).IsEqualTo("add_to_cart");
        await Assert.That(result.ToolCalls[1].Result).IsEqualTo("ok");
        await Assert.That(result.ToolCalls[1].Arguments!["productId"]).IsEqualTo(7);
    }

    [Test]
    public async Task ToolCallWithNoResultHasNullResultAndIsNotAnError()
    {
        var result = AgentRunMapper.ToResponse(RunWith(
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_products")])
        ]));

        await Assert.That(result.ToolCalls.Single().Result).IsNull();
        await Assert.That(result.ToolCalls.Single().IsError).IsFalse();
    }

    [Test]
    public async Task ToolCallWhoseResultHasAnExceptionIsFlaggedAsAnError()
    {
        var result = AgentRunMapper.ToResponse(RunWith(
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "delete_product")]),
            new ChatMessage(ChatRole.Tool,
                [new FunctionResultContent("call-1", "failed") { Exception = new InvalidOperationException("boom") }])
        ]));

        await Assert.That(result.ToolCalls.Single().IsError).IsTrue();
    }

    [Test]
    public async Task UsageElapsedAndPromptVersionAreMapped()
    {
        var result = AgentRunMapper.ToResponse(RunWith([],
            new UsageDetails { InputTokenCount = 120, OutputTokenCount = 30, TotalTokenCount = 150 }));

        await Assert.That(result.Usage!.InputTokens).IsEqualTo(120);
        await Assert.That(result.Usage.OutputTokens).IsEqualTo(30);
        await Assert.That(result.Usage.TotalTokens).IsEqualTo(150);
        await Assert.That(result.ElapsedMs).IsEqualTo(250);
        await Assert.That(result.PromptVersion).IsEqualTo(AgentPrompts.CustomerVersion);
        await Assert.That(result.Text).IsEqualTo("done");
    }

    [Test]
    public async Task MissingUsageStaysNull()
    {
        var result = AgentRunMapper.ToResponse(RunWith([]));

        await Assert.That(result.Usage).IsNull();
    }

    [Test]
    public async Task ToolOnlyMessagesHaveNullTextAndKeepTheirRole()
    {
        var result = AgentRunMapper.ToResponse(RunWith(
        [
            new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_products")]),
            new ChatMessage(ChatRole.Assistant, "Here you go.")
        ]));

        await Assert.That(result.Messages[0].Role).IsEqualTo("assistant");
        await Assert.That(result.Messages[0].Text).IsNull();
        await Assert.That(result.Messages[1].Text).IsEqualTo("Here you go.");
    }
}
