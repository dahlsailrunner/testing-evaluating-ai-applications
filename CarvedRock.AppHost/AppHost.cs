var builder = DistributedApplication.CreateBuilder(args);

// pgvector build of Postgres 18: the knowledge base stores embeddings in a vector column
var db = builder.AddPostgres("db")
    .WithImage("pgvector/pgvector")
    .WithImageTag("0.8.7-pg18")
    .WithUrlForEndpoint("tcp", u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly)
    .AddDatabase("CarvedRockPostgres");

var smtp = builder.AddMailPit("smtp")
    .WithUrlForEndpoint("http", u => u.DisplayText = "Email Inbox")
    .WithUrlForEndpoint("smtp", u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly);

var openAiKeyParam = builder.AddParameter("openaiKey", 
        value: builder.Configuration["Parameters:openaiKey"] ?? "", secret: true)
    .WithDescription("OpenAI API Key.  Get one from " +
    "[OpenAI](https://platform.openai.com). Note " +
     "that without this set, all features except the AI chat, the " +
     "Agent API and knowledge search (which needs embeddings) will still work fine.",
                enableMarkdown: true);

// Off by default. When true the API also seeds a product and a knowledge document that carry
// prompt-injection payloads, for adversarial testing of the AI features. Set it with:
//   aspire secret set Parameters:includeAdversarialContent true
var adversarialParam = builder.AddParameter("includeAdversarialContent",
        value: builder.Configuration["Parameters:includeAdversarialContent"] ?? "false")
    .WithDescription("Set to `true` to also seed prompt-injection test content (a product and a " +
                     "help document) for adversarial testing of the AI features. Leave `false` " +
                     "for normal use.",
                enableMarkdown: true);

var api = builder.AddProject<Projects.CarvedRock_Api>("api")
    .WithEnvironment("AIConnection__OpenAIKey", openAiKeyParam)
    .WithEnvironment("Seed__IncludeAdversarialContent", adversarialParam)
    .WithUrlForEndpoint("https", url => url.DisplayText = "API Scalar")
    .WithHttpHealthCheck("/alive")
    .WithReference(db)
    .WaitFor(db)
    .WithReference(smtp)
    .WaitFor(smtp)
    .WithHttpCommand(
        path: "/internal/reset-data",
        displayName: "Reset Data",
        commandOptions: new HttpCommandOptions()
        {
            Description = """
                Resets data.  All changes wiped out and database 
                is fully reset to original state and list of products!
                """,
            IconName = "DatabaseLightning",
            IsHighlighted = true
        });

var mcp = builder.AddProject<Projects.CarvedRock_Mcp>("mcp")
    .WithUrlForEndpoint("https", u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly)
    .WithHttpHealthCheck("/alive")
    .WithReference(api)
    .WaitFor(api);

var agent = builder.AddProject<Projects.CarvedRock_Agent>("agent")
    .WithHttpHealthCheck("/alive")
    .WithUrlForEndpoint("https", u => u.DisplayText = "Agent Scalar")
    .WithEnvironment("AIConnection__OpenAIKey", openAiKeyParam)
    .WithReference(mcp)
    .WaitFor(mcp);

var webapp = builder.AddProject<Projects.CarvedRock_WebApp>("webapp")
    .WithUrlForEndpoint("https", u => u.DisplayText = "Web App")
    .WithHttpHealthCheck("/alive")
    .WithReference(api)
    .WithReference(agent)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.AddMcpInspector("mcp-inspector")
    .WithMcpServer(mcp, path: "")
    .WithUrlForEndpoint("client", u => u.DisplayText = "MCP Inspector")
    .WithUrlForEndpoint("server-proxy", u => u.DisplayLocation = UrlDisplayLocation.DetailsOnly);

builder.Build().Run();
