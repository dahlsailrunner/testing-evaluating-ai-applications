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
builder.Services.AddScoped<Agent>();

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

app.Run();

static void CustomizeProblemDetails(ProblemDetailsContext context)
{
    context.ProblemDetails.Detail = "Provide the instance value when contacting us for support";
    context.ProblemDetails.Instance = Activity.Current?.RootId;
}
