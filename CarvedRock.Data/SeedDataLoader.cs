using System.Text.Json;
using CarvedRock.Data.Entities;

namespace CarvedRock.Data;

public static class SeedDataLoader
{
    private const string AdversarialResourceName = "AdversarialSeedData.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The normal product seed, optionally followed by the adversarial test products (see
    /// <see cref="LoadAdversarialProducts"/>). With <paramref name="includeAdversarialContent"/> false
    /// - the default everywhere - the result is exactly the contents of SeedData.json.
    /// </summary>
    public static List<Product> LoadProducts(bool includeAdversarialContent = false)
    {
        // Can you help me generate some data for Product?  I'd like 50 products,
        // and each of them should be something that might be found at an outdoor
        // recreational equipment store.  The categories for them should be one of
        // "boots", "equipment", and "kayaks". Image Urls should be something from
        // the service picsum.photos. Use JSON format and exclude the ID property.
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SeedData.json"));
        var products = JsonSerializer.Deserialize<List<Product>>(json, JsonOptions) ?? [];

        if (includeAdversarialContent) products.AddRange(LoadAdversarialProducts());
        return products;
    }

    /// <summary>
    /// Products whose descriptions carry a prompt-injection payload aimed at the AI assistant's tools.
    /// Teaching content for adversarial testing, only ever seeded when explicitly switched on.
    /// </summary>
    public static List<Product> LoadAdversarialProducts()
    {
        using var stream = typeof(SeedDataLoader).Assembly.GetManifestResourceStream(AdversarialResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {AdversarialResourceName} is missing.");
        return JsonSerializer.Deserialize<List<Product>>(stream, JsonOptions) ?? [];
    }
}
