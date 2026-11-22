using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace CarvedRock.EvalTests;

// The category lets test-with-coverage.ps1 leave the (slow, paid, 
// LLM-judged) evals out of a normal run.
[Category("Evals")]
[ClassDataSource<EvalFixture>(Shared = SharedType.PerTestSession)]
public class ReturnPolicyEvalTests(EvalFixture fixture)
{
    [Test]
    public async Task ReturnWindowQuestionIsAnsweredFromTheKnowledgeBase()
    {
        const string question = "How long do I have to return unworn boots?";

        // Ask the real agent, exactly as the web app would.
        var response = await fixture.CreateHttpClient("agent")
            .PostAsJsonAsync("/agent/run", new { message = question });
        var run = (await response.Content.ReadFromJsonAsync<AgentRun>())!;

        // 1. Deterministic: did it look the answer up?
        await Assert.That(run.ToolCalls.Select(t => t.Name))
                        .Contains("search_knowledge");

        // 2. Model-judged: does the answer actually address the question?
        // evaluators configured in the Reporting property
        await using var scenario = await fixture.Reporting.CreateScenarioRunAsync(
            "ReturnPolicy.UnwornBoots", additionalTags: [run.PromptVersion]);

        var result = await scenario.EvaluateAsync(
            [new ChatMessage(ChatRole.User, question)],
            new ChatResponse(new ChatMessage(ChatRole.Assistant, run.Text)));

        var relevance = result.Get<NumericMetric>
                (RelevanceEvaluator.RelevanceMetricName);

        // No score at all means the judge itself failed; 
        // that is a failure too, not a pass.
        var score = relevance.Value ?? throw new Exception($"The judge returned " +
                $"no relevance score: {relevance.Reason}");
        await Assert.That(score).IsGreaterThanOrEqualTo(4); // scores are 1-5
    }
}

// Local copy of the JSON contract, on purpose (same convention as AppTests).
public record AgentRun(string Text, string PromptVersion,
                        List<AgentToolCall> ToolCalls);
public record AgentToolCall(string Name);
