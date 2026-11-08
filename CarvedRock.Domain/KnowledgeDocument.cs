namespace CarvedRock.Domain;

/// <summary>A source document of the knowledge base (markdown).</summary>
/// <param name="Id">Stable id: the file name without extension.</param>
public record KnowledgeDocument(string Id, string Title, string Markdown);

/// <summary>One section of a document, ready to be embedded.</summary>
public record KnowledgeChunkText(string DocumentId, string DocumentTitle, string Heading, string Content)
{
    /// <summary>
    /// The exact text that gets embedded. Title and heading are included so a short section
    /// ("Sizing") still carries enough context to be found by a question about its topic.
    /// </summary>
    public string EmbeddingInput => $"{DocumentTitle} - {Heading}\n{Content}";
}
