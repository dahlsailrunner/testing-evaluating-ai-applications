using CarvedRock.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TUnit.AspNetCore;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace CarvedRock.ApiTests.Utils;

public class ApiFactory : TestWebApplicationFactory<Program>
{
    [ClassDataSource<TestData>(Shared = SharedType.PerTestSession)]
    public TestData TestData { get; init; } = null!;

    private WireMockServer _wireMockDadJokes = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>
                                (TestAuthHandler.SchemeName, _ => { });

            // no OpenAI in these tests - deterministic embeddings instead (see TestData for ingestion)
            services.RemoveAll<IEmbeddingGenerator<string, Embedding<float>>>();
            services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>, HashingEmbeddingGenerator>();

            // no SMTP server in these tests - record emails instead of sending them
            services.AddSingleton<RecordingOrderEmailSender>();
            services.AddScoped<IOrderEmailSender>(sp =>
                sp.GetRequiredService<RecordingOrderEmailSender>());
        });

        _wireMockDadJokes = WireMockServer.Start();

        _wireMockDadJokes
            .Given(Request.Create().WithPath("/").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithBody("""{"id": "xxxxxx", "joke": "joke's on you - from the mock!", "status": 200}"""));

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "DadJokeUrl", _wireMockDadJokes.Url },
                // TestData ingests the knowledge base once, up front; don't let every
                // per-test host race to do it again at startup
                { "Knowledge:IngestOnStartup", "false" }
            });
        });
    }

    protected override void ConfigureStartupConfiguration(
                        IConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "ConnectionStrings:CarvedRockPostgres",
                        TestData.ConnectionString }
        });
    }
}

public abstract class ApiTestsBase : WebApplicationTest<ApiFactory, Program>
{
    protected TestData TestData => GlobalFactory.TestData;
    protected static DefaultLogger TestLogger =>
                        TestContext.Current!.GetDefaultLogger();
}