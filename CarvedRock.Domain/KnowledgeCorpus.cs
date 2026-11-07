namespace CarvedRock.Domain;

/// <summary>
/// The knowledge base source documents: markdown files under <c>Knowledge/</c>, embedded in
/// this assembly so they travel with every host (API, tests) without file-copy settings.
/// </summary>
public static class KnowledgeCorpus
{
    private const string ResourcePrefix = "Knowledge/";

    public static List<KnowledgeDocument> Load()
    {
        var assembly = typeof(KnowledgeCorpus).Assembly;

        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".md"))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                var markdown = reader.ReadToEnd();

                var id = Path.GetFileNameWithoutExtension(name);
                return new KnowledgeDocument(id, TitleOf(markdown) ?? id, markdown);
            })
            .ToList();
    }

    private static string? TitleOf(string markdown) =>
        markdown.ReplaceLineEndings("\n").Split('\n')
            .FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim();
}
