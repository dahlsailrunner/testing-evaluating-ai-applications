using CarvedRock.Domain;

namespace CarvedRock.Api;

/// <summary>
/// Embeds the knowledge base documents when the API starts. Unchanged chunks are skipped (see
/// <see cref="KnowledgeLogic"/>), so a restart costs nothing once the table is populated.
/// Failure - typically no OpenAI key configured - is logged, never fatal: everything except
/// knowledge search keeps working, and search answers 503 until ingestion succeeds.
/// </summary>
public class KnowledgeIngestionService(IServiceScopeFactory scopeFactory, IConfiguration config,
    ILogger<KnowledgeIngestionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Knowledge:IngestOnStartup", true))
        {
            logger.LogInformation("Knowledge ingestion on startup is disabled.");
            return;
        }

        using var scope = scopeFactory.CreateScope();
        await IngestSafelyAsync(scope.ServiceProvider.GetRequiredService<IKnowledgeLogic>(),
            config.GetValue<bool>("Seed:IncludeAdversarialContent"), logger, stoppingToken);
    }

    /// <returns>True if the knowledge base is now up to date.</returns>
    public static async Task<bool> IngestSafelyAsync(IKnowledgeLogic knowledge, bool includeAdversarialContent,
        ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await knowledge.IngestAsync(KnowledgeCorpus.Load(includeAdversarialContent), cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Knowledge base ingestion failed (is an OpenAI key configured?). Knowledge search " +
                "will be unavailable until it succeeds; POST /internal/reset-data retries it.");
            return false;
        }
    }
}
