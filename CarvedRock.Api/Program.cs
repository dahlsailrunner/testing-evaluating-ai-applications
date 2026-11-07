using CarvedRock.Api;
using CarvedRock.Core;
using CarvedRock.Data;
using CarvedRock.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddValidatorsFromAssemblyContaining<NewProductValidator>();
builder.Services.AddProblemDetails(opts => opts.CustomizeProblemDetails = CustomizeProblemDetails);
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();

JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.Authority = builder.Configuration.GetValue<string>("Auth:Authority"); ;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "email",
            ValidateAudience = false
        };
    });
builder.Services.AddTransient<IClaimsTransformation, AdminClaimsTransformation>();

builder.Services.AddControllers();

var oauthScopes = new Dictionary<string, string>
{
    { "api", "Resource access: Carved Rock API" },
    { "openid", "OpenID information" },
    { "profile", "User profile information" },
    { "email", "User email address" }
};

builder.Services.AddOpenApiWithAuth(builder.Configuration.GetValue<string>("Auth:Authority")!,
    oauthScopes);

builder.Services.AddScoped<IProductLogic, ProductLogic>();
builder.Services.AddScoped<ICartLogic, CartLogic>();
builder.Services.AddScoped<IOrderLogic, OrderLogic>();

// Order-confirmation email now lives in the API (moved out of the web app).
// https://aspire.dev/integrations/custom-integrations/client-integrations/
builder.AddMailKitClient("smtp");
builder.Services.AddScoped<IOrderEmailSender, EmailService>();

builder.AddNpgsqlDbContext<LocalContext>("CarvedRockPostgres",
    configureDbContextOptions: options => options.UseNpgsql(npgsql => npgsql.UseVector())); // pgvector (see UseCarvedRockNpgsql)

builder.Services.AddScoped<ICarvedRockRepository, CarvedRockRepository>();

// Knowledge base search: embeddings come from OpenAI (same key as the agent), vectors live in
// Postgres via pgvector. Without a key everything else still works; search answers 503.
builder.AddOpenAIClient("kyt-OpenAI", configureSettings: settings =>
{
    settings.EnableSensitiveTelemetryData = true;
    settings.Key = builder.Configuration.GetValue<string>("AIConnection:OpenAIKey");
}).AddEmbeddingGenerator(
    builder.Configuration.GetValue<string>("AIConnection:EmbeddingModel") ?? "text-embedding-3-small");

builder.Services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
builder.Services.AddScoped<IKnowledgeLogic, KnowledgeLogic>();
builder.Services.AddHostedService<KnowledgeIngestionService>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    SetupDevelopment(app, oauthScopes);
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers().RequireAuthorization();

app.Run();

static void SetupDevelopment(WebApplication app, Dictionary<string, string> oauthScopes)
{
    using var scope = app.Services.CreateScope();

    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<LocalContext>();

    // Off by default. When on, a product and a knowledge document carrying prompt-injection payloads
    // are added for adversarial testing of the AI features (see the readme).
    var includeAdversarial = app.Configuration.GetValue<bool>("Seed:IncludeAdversarialContent");
    if (includeAdversarial)
    {
        app.Logger.LogWarning("Seed:IncludeAdversarialContent is ON: the data includes prompt-injection " +
                              "test content. This is for adversarial testing only.");
    }
    context.MigrateAndCreateData(includeAdversarialContent: includeAdversarial);

    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .AddPreferredSecuritySchemes("oauth2")
        .AddAuthorizationCodeFlow("oauth2", flow =>
        {
            flow.ClientId = "interactive.public";
            flow.Pkce = Pkce.Sha256;
            flow.SelectedScopes = [.. oauthScopes.Keys];
        }));

    app.MapPost("/internal/reset-data", async (LocalContext db, IKnowledgeLogic knowledge,
        IConfiguration config, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
    {
        var includeAdversarial = config.GetValue<bool>("Seed:IncludeAdversarialContent");
        db.MigrateAndCreateData(includeAdversarialContent: includeAdversarial);

        // Resetting only reseeds products, but it's also the retry button for knowledge ingestion
        // (e.g. the OpenAI key was added after startup). Unchanged chunks are skipped, so it's cheap.
        await KnowledgeIngestionService.IngestSafelyAsync(knowledge, includeAdversarial,
            loggerFactory.CreateLogger<KnowledgeIngestionService>(), cancellationToken);
        return Results.NoContent();
    });
}

static void CustomizeProblemDetails(ProblemDetailsContext context)
{
    // Server errors always get the generic message (don't leak internals). For other statuses
    // keep a detail the endpoint set deliberately (e.g. "order has already shipped") so callers
    // - including the MCP tools and the AI agent behind them - can see why a request was refused.
    if (context.ProblemDetails.Status >= 500 || string.IsNullOrEmpty(context.ProblemDetails.Detail))
    {
        context.ProblemDetails.Detail = "Provide the instance value when contacting us for support";
    }
    context.ProblemDetails.Instance = Activity.Current?.RootId;
}
