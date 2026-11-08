using CarvedRock.Agent;
using CarvedRock.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.IdentityModel.Tokens;
using OpenAI;
using System.Diagnostics;
using Scalar.AspNetCore;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails(opts => opts.CustomizeProblemDetails = CustomizeProblemDetails);

builder.AddOpenAIClient("kyt-OpenAI", configureSettings: settings =>
{
    settings.EnableSensitiveTelemetryData = true;
    settings.Key = builder.Configuration.GetValue<string>("AIConnection:OpenAIKey");
}).AddChatClient(builder.Configuration.GetValue<string>("AIConnection:OpenAIModel"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IMcpClientProvider, McpClientProvider>();
builder.Services.AddScoped<Agent>();
builder.Services.AddScoped<ProductDescriber>();

var authority = builder.Configuration.GetValue<string>("Auth:Authority");
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = authority;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "email",
            ValidateAudience = false
        };
    });
builder.Services.AddAuthorization();

var oauthScopes = new Dictionary<string, string>
{
    { "api", "Resource access: Carved Rock API" },
    { "openid", "OpenID information" },
    { "profile", "User profile information" },
    { "email", "User email address" }
};

builder.Services.AddOpenApiWithAuth(builder.Configuration.GetValue<string>("Auth:Authority")!,
    oauthScopes);

builder.Services.AddTransient<IClaimsTransformation, AdminClaimsTransformation>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .AddPreferredSecuritySchemes("oauth2")
        .AddAuthorizationCodeFlow("oauth2", flow =>
        {
            flow.ClientId = "interactive.public";
            flow.Pkce = Pkce.Sha256;
            flow.SelectedScopes = [.. oauthScopes.Keys];
        }));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/agent", (AgentChatRequest request, Agent agent, CancellationToken cancellationToken) =>
    agent.GetAgentResponse(request.Message, request.History, cancellationToken));

// Non-streaming variant: same request, same auth as /agent, but the response includes the
// tool calls the agent made, token usage, elapsed time, and the prompt version.
app.MapPost("/agent/run", async (AgentChatRequest request, Agent agent, CancellationToken cancellationToken) =>
    AgentRunMapper.ToResponse(await agent.RunAsync(request.Message, request.History, cancellationToken)));

// Generative (non-agentic) feature: no tools, no conversation - drafts a catalog description from a
// few product facts. Admin only (anonymous callers get 401, customers 403). The request is checked
// before the model is called; the draft comes back as the model wrote it, with any rule violations
// listed rather than silently fixed.
app.MapPost("/agent/describe", async (DescribeProductRequest request, ProductDescriber describer,
        CancellationToken cancellationToken) =>
    {
        var errors = request.Validate();
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        try
        {
            return Results.Ok(await describer.DescribeAsync(request, cancellationToken));
        }
        catch (ProductDescriptionUnavailableException ex)
        {
            return Results.Problem(title: "A product description could not be drafted",
                detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    })
    .RequireAuthorization(policy => policy.RequireRole("admin"));

app.Run();

static void CustomizeProblemDetails(ProblemDetailsContext context)
{
    // Server errors always get the generic message (don't leak internals); for other statuses keep a
    // detail the endpoint set deliberately.
    if (context.ProblemDetails.Status >= 500 || string.IsNullOrEmpty(context.ProblemDetails.Detail))
    {
        context.ProblemDetails.Detail = "Provide the instance value when contacting us for support";
    }
    context.ProblemDetails.Instance = Activity.Current?.RootId;
}
