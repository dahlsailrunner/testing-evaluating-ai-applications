using CarvedRock.Data;
using Duende.IdentityModel.Client;
using ModelContextProtocol.Client;
using Projects;
using TUnit.Aspire;
using Bogus;

[assembly: Retry(2, BackoffMs = 30_000)]

namespace CarvedRock.AppTests.Utils;

public class AppFixture : AspireFixture<CarvedRock_AppHost>
{
    protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(3);

    public LocalContext TestDbContext { get; private set; } = null!;

    public List<Product> InitialProducts { get; private set; } = null!;

    public Faker GeneralFaker = new();
    public readonly Faker<NewProductModel> NewProductFaker =
                        new Faker<NewProductModel>()
        .RuleFor(p => p.Name, f => f.Commerce.ProductName())
        .RuleFor(p => p.Description, f => f.Commerce.ProductDescription())
        .RuleFor(p => p.Category, f => f.PickRandom("boots", "equip", "kayak"))
        .RuleFor(p => p.Price, (f, p) =>
                p.Category == "boots" ? f.Random.Double(50, 300) :
                p.Category == "equip" ? f.Random.Double(20, 150) :
                p.Category == "kayak" ? f.Random.Double(100, 500) : 0)
        .RuleFor(p => p.ImgUrl, f => f.Image.PicsumUrl());

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync(); // Build, start, wait for resources

        // Post-start: get already-seeded data for test confirmations
        //     could also do migrations or create test data
        var connStr = await App.GetConnectionStringAsync("CarvedRockPostgres");

        var options = new DbContextOptionsBuilder<LocalContext>()
                            .UseNpgsql(connStr)
                            .Options;
        TestDbContext = new LocalContext(options);

        InitialProducts = await TestDbContext.Products.Select(p =>
                new Product(p.Id, p.Name, p.Category, p.Description,
                            p.Price, p.ImgUrl))
            .ToListAsync(); ;
    }

    public async Task<HttpClient> GetAdminApiClient()
    {
        var client = App.CreateHttpClient("api");
        var token = await GetClientCredsAccessTokenAsync("m2m.short", "secret"); // admin is m2m.short
        client.SetBearerToken(token); // Duende.IdentityModel convenience method

        return client;
    }

    public async Task<HttpClient> GetCustomerApiClient()
    {
        var client = App.CreateHttpClient("api");
        var token = await GetClientCredsAccessTokenAsync("m2m", "secret"); // admin is m2m.short
        client.SetBearerToken(token); // Duende.IdentityModel convenience method

        return client;
    }

    public async Task<McpClient> GetAnonymousMcpClient()
    {
        var clientTransport = new HttpClientTransportOptions
        {
            Endpoint = App.GetEndpoint("mcp", "https"),
            TransportMode = HttpTransportMode.StreamableHttp
        };
        return await McpClient.CreateAsync(new HttpClientTransport(clientTransport));
    }

    public async Task<McpClient> GetAdminMcpClient()
    {
        var clientTransport = new HttpClientTransportOptions
        {
            Endpoint = App.GetEndpoint("mcp", "https"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string>()
        };
        var accessToken = await GetClientCredsAccessTokenAsync("m2m.short",
                                                "secret");

        clientTransport.AdditionalHeaders.Add("Authorization", $"Bearer {accessToken}");
        return await McpClient.CreateAsync(new HttpClientTransport(clientTransport));
    }

    // for machine-to-machine generally - but that's what works without
    // a UI-based interaction
    public static async Task<string> GetClientCredsAccessTokenAsync(
        string clientId, string secret,
        string scope = "api", CancellationToken cancellationToken = default)
    {
        var idSrvRoot = new Uri("https://demo.duendesoftware.com");  // your idp
        var client = new HttpClient { BaseAddress = idSrvRoot };

        var response = await client.RequestClientCredentialsTokenAsync(
            new ClientCredentialsTokenRequest
            {
                Address = "connect/token",

                ClientId = clientId,
                ClientSecret = secret,
                Scope = scope,
            }, cancellationToken);

        if (response.IsError)
        {
            throw new Exception($"Error retrieving access " +
                        "token for clientId {clientId}: {response.Error}");
        }

        return response.AccessToken!;
    }

    // NOT USED - would require an IDP that supports "resource owner" flow, but this
    //   would enable more real-life user access tokens
    public async Task<string> GetUserAccessTokenAsync(string username, string password,
        string scope = "openid profile email api",
        CancellationToken cancellationToken = default)
    {
        var idSrvRoot = App.GetEndpoint("idsrv", "https"); // your idp within aspire
        var client = new HttpClient { BaseAddress = idSrvRoot };

        var response = await client.RequestPasswordTokenAsync(
            new PasswordTokenRequest
            {
                Address = "connect/token",

                ClientId = "testing.confidential",
                ClientSecret = "secret",
                Scope = scope,

                UserName = username,
                Password = password
            }, cancellationToken);

        if (response.IsError)
        {
            throw new Exception($"Error retrieving access " +
                    "token for user {username}: {response.Error}");
        }

        return response.AccessToken!;
    }
}

// I define a record here so that I can ensure the current "contract"
// of what the tests expect are clearly documented and tested rather
// than relying on the code (which can change)
public record Product(int Id, string Name, string Category,
                      string Description, double Price, string ImgUrl);
public record NewProductModel(string Name, string Category,
                      string Description, double Price, string ImgUrl);