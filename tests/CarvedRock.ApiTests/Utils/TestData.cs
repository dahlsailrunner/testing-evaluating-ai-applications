using Bogus;
using CarvedRock.Core;
using CarvedRock.Data;
using CarvedRock.Domain.Mapping;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core.Interfaces;

namespace CarvedRock.ApiTests.Utils;

public class TestData : IAsyncInitializer, IAsyncDisposable
{
    // Testcontainers asks Docker for a *random* published host port. On Windows that
    // port occasionally lands in a range the OS has reserved (Hyper-V/WSL grab blocks
    // of the ephemeral range - see `netsh int ipv4 show excludedportrange protocol=tcp`),
    // and the WSL port relay then has no way to forward it. Docker still reports the
    // mapping happily, so the container looks started while every connection from the
    // test process fails. That is the classic "passes most of the time" Testcontainers
    // flake on a Windows dev box.
    //
    // The important consequence: waiting longer can never fix a port the OS refuses to
    // forward - only a *different* port can. So the readiness budget is deliberately
    // short and a failure is retried with a brand new container. At roughly a 3% chance
    // per container, three attempts makes this a non-event.
    private const int StartAttempts = 3;
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(20);

    private PostgreSqlContainer _dbContainer = null!;

    public PostgreSqlContainer DbContainer => _dbContainer;

    public string ConnectionString =>
        TuneForTests(_dbContainer.GetConnectionString(),
                     connectTimeoutSeconds: 30, pooling: true);

    public List<Data.Entities.Product> InitialProducts { get; private set; } = null!;

    public readonly Faker<NewProductModel> NewProductFaker = new Faker<NewProductModel>()
        .UseSeed(2001) // will generate consistent data (with any fixed seed value)
        .RuleFor(p => p.Name, f => f.Commerce.ProductName())
        .RuleFor(p => p.Description, f => f.Commerce.ProductDescription())
        .RuleFor(p => p.Category, f => f.PickRandom("boots", "equip", "kayak"))
        .RuleFor(p => p.Price, (f, p) =>
                p.Category == "boots" ? f.Random.Double(50, 300) :
                p.Category == "equip" ? f.Random.Double(20, 150) :
                p.Category == "kayak" ? f.Random.Double(100, 500) : 0)
        .RuleFor(p => p.ImgUrl, f => f.Image.PicsumUrl());

    public readonly Faker GeneralFaker = new();
    public async Task InitializeAsync()
    {
        _dbContainer = await StartReachableContainerAsync();

        var options = new DbContextOptionsBuilder<LocalContext>()
                            // The API gets retries for free from Aspire's
                            // AddNpgsqlDbContext; this hand-built context has to ask
                            // for them. Without it, EF wraps the first transient
                            // connect blip in "An exception has been raised that is
                            // likely due to a transient failure" - and because this
                            // initializer runs once per test session, that single blip
                            // fails every test in the project.
                            .UseNpgsql(ConnectionString, npgsql => npgsql
                                .EnableRetryOnFailure(
                                    maxRetryCount: 5,
                                    maxRetryDelay: TimeSpan.FromSeconds(10),
                                    errorCodesToAdd: null))
                            .Options;
        await using var context = new LocalContext(options);

        // Any data prep / migrations / setup can go here

        //context.MigrateAndCreateData(force: true); // or completely customize!!!
        await context.Database.EnsureCreatedAsync();

        var products = NewProductFaker.Generate(100);
        var productMapper = new ProductMapper();

        List<Data.Entities.Product> productsToCreate = [];
        foreach (var product in products)
        {
            productsToCreate.Add(productMapper.NewProductModelToProduct(product));
        }

        context.Products.AddRange(productsToCreate);
        await context.SaveChangesAsync();

        InitialProducts = await context.Products.ToListAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // clean up / reset persistent data?

        // Stop the container explicitly rather than leaving it to the Testcontainers
        // resource reaper. Leaked containers from earlier runs quietly compete for
        // Docker's CPU/memory budget, which is itself a source of slow-startup flake.
        if (_dbContainer is not null)
        {
            await _dbContainer.DisposeAsync();
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Starts Postgres and does not return until the test process can actually query it
    /// over the published port. A container that never becomes reachable is thrown away
    /// and replaced, so a bad random port costs seconds instead of the whole session.
    /// </summary>
    private static async Task<PostgreSqlContainer> StartReachableContainerAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            var container = BuildContainer();
            try
            {
                await container.StartAsync();
                return container;
            }
            catch (Exception ex)
            {
                // Always tear the failed container down - including on the last
                // attempt. A leaked container keeps holding Docker resources long
                // after the session has given up on it.
                await container.DisposeAsync();

                if (attempt >= StartAttempts)
                {
                    throw;
                }

                Console.WriteLine(
                    $"Postgres container was not reachable on attempt {attempt} of " +
                    $"{StartAttempts} ({ex.GetType().Name}: {ex.Message}). " +
                    "Retrying with a freshly published port.");
            }
        }
    }

    private static PostgreSqlContainer BuildContainer() =>
        new PostgreSqlBuilder("postgres:18.3")
            // Replaces the module's default `pg_isready` probe, which runs *inside* the
            // container and so proves only that Postgres is up - never that the host
            // can reach it. The probe below is strictly stronger: it subsumes
            // pg_isready and also exercises the published port.
            .WithWaitStrategy(Wait.ForUnixContainer()
                .AddCustomWaitStrategy(new HostCanQueryPostgres(), strategy => strategy
                    .WithTimeout(ReadyTimeout)
                    .WithInterval(TimeSpan.FromSeconds(1))))
            .Build();

    /// <summary>
    /// Adjusts the connection string Testcontainers hands us for the realities of a
    /// developer machine. Nothing here changes what the tests observe - only how
    /// patiently, and over which connection, the driver talks to Postgres.
    /// </summary>
    private static string TuneForTests(string containerConnectionString,
                                       int connectTimeoutSeconds, bool pooling)
        => new NpgsqlConnectionStringBuilder(containerConnectionString)
        {
            // The test container has no server certificate.
            SslMode = SslMode.Disable,

            // Npgsql's 15-second default is measured against a healthy local Postgres,
            // not a first connection through a WSL/VM port relay on a loaded (or
            // security-instrumented) box.
            Timeout = connectTimeoutSeconds,
            CommandTimeout = 60,

            // Probe connections turn pooling off so they don't leave physical
            // connections parked in a pool nobody reuses.
            Pooling = pooling
        }.ConnectionString;

    /// <summary>
    /// Readiness probe that connects the way the tests will - real driver, published
    /// port, from the host - and runs a trivial query.
    /// </summary>
    private sealed class HostCanQueryPostgres : IWaitUntil
    {
        public async Task<bool> UntilAsync(IContainer container)
        {
            // A short per-attempt timeout keeps the loop polling instead of blocking on
            // one slow attempt; the wait strategy owns the overall time budget.
            var connectionString = TuneForTests(
                ((PostgreSqlContainer)container).GetConnectionString(),
                connectTimeoutSeconds: 5, pooling: false);

            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("SELECT 1", connection);
                await command.ExecuteScalarAsync();
                return true;
            }
            catch (Exception)
            {
                // Any failure here just means "not ready yet" - keep polling until the
                // strategy's timeout, which then triggers a retry on a new port.
                return false;
            }
        }
    }
}
