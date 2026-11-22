using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Projects;
using TUnit.Aspire;

namespace CarvedRock.EvalTests.Utils;
public class EvalFixture : AspireFixture<CarvedRock_AppHost>
{
    protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(3);

    // The MCP Inspector is a developer tool for poking at the MCP server
    // by hand. Tests never use it, so leave it out rather than start it,
    // and wait on it, for every run.
    protected override IEnumerable<string> ResourcesToRemove() => ["mcp-inspector"];

    public ReportingConfiguration Reporting { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();    // start the whole app, same as AppTests

        // "healthy" doesn't mean "ingested" (see below)
        await WaitForKnowledgeBaseAsync();  

        Reporting = CreateReportingConfiguration();
    }

    // Aspire reports the API healthy as soon as Kestrel is up, but the help
    // documents are embedded by a background service afterwards. Until that
    // finishes, search_knowledge finds nothing and the eval would fail for
    // a reason that has nothing to do with the prompt. Ingestion is one
    // transaction, so "has results" really does mean "fully ingested".
    private async Task WaitForKnowledgeBaseAsync()
    {
        var api = CreateHttpClient("api");
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var response = await api.GetAsync("/knowledge/search?q=returns&top=1");
            if (response.IsSuccessStatusCode &&
                (await response.Content.ReadAsStringAsync()).Contains("documentId"))
                return;
            await Task.Delay(2_000);
        }
        throw new Exception("The knowledge base was not ingested within 2 minutes. " +
                        "Is the OpenAI key configured?");
    }

    private ReportingConfiguration CreateReportingConfiguration()
    {
        // https://learn.microsoft.com/en-us/dotnet/ai/evaluation/libraries

        var key = App.Services.GetRequiredService<IConfiguration>()
                                                ["Parameters:openaiKey"];
        if (string.IsNullOrEmpty(key))
            throw new Exception("Set Parameters:openaiKey - the judge model needs it.");

        // The judge is a different model from the one the agent uses, so it 
        // isn't grading its own homework.
        var judge = new OpenAIClient(key).GetChatClient("gpt-4o").AsIChatClient();

        return DiskBasedReportingConfiguration.Create(
            storageRootPath: Path.GetFullPath("../../../../../EvalResults", 
                                    AppContext.BaseDirectory),
            evaluators: [new RelevanceEvaluator()],
            chatConfiguration: new ChatConfiguration(judge),
            enableResponseCaching: false,
            executionName: $"{DateTime.Now:yyyyMMddTHHmmss}"); 
    }
}
