using System.Text;
using System.Text.RegularExpressions;
using CarvedRock.Data.Entities;
using Microsoft.Extensions.AI;

namespace CarvedRock.ApiTests.Utils;

/// <summary>
/// Deterministic stand-in for the OpenAI embedding model in API tests. Each word is hashed to a
/// slot of a fixed-width vector (a hashed bag of words), then the vector is length-normalized.
/// Texts that share words therefore end up close together under cosine distance, so searches
/// rank by keyword overlap. That is enough to prove the plumbing - ingest, store in pgvector,
/// nearest-neighbour query, ordering, limits - against a real database without a network call.
/// It says nothing about *semantic* quality; that is what the real model (and the AppTests,
/// and retrieval evaluation) are for.
/// </summary>
public partial class HashingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "the", "is", "are", "do", "does", "i", "my", "me", "to", "of", "in", "for", "on",
        "and", "or", "how", "what", "can", "you", "we", "it", "with", "at", "be", "by", "should", "if"
    ];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var dimensions = options?.Dimensions ?? KnowledgeChunk.EmbeddingDimensions;
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
            values.Select(text => new Embedding<float>(Embed(text, dimensions)))));
    }

    private static float[] Embed(string text, int dimensions)
    {
        var vector = new float[dimensions];
        foreach (Match match in WordPattern().Matches(text.ToLowerInvariant()))
        {
            var word = match.Value;
            if (word.Length < 2 || StopWords.Contains(word)) continue;
            if (word.Length > 3 && word.EndsWith('s')) word = word[..^1]; // crude plural folding

            vector[(int)(StableHash(word) % (uint)dimensions)] += 1f;
        }

        var length = MathF.Sqrt(vector.Sum(v => v * v));
        if (length == 0f) vector[0] = 1f; // nothing to hash; still a valid unit vector
        else
            for (var i = 0; i < vector.Length; i++) vector[i] /= length;
        return vector;
    }

    // FNV-1a: string.GetHashCode() is randomized per process, which would make results flaky
    private static uint StableHash(string word)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(word))
        {
            hash ^= b;
            hash *= 16777619u;
        }
        return hash;
    }

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex WordPattern();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

/// <summary>Simulates the embedding model being unreachable (no key, network down, quota...).</summary>
public class FailingEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new HttpRequestException("The embedding service is unreachable (simulated).");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}
