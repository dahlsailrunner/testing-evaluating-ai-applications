namespace CarvedRock.Domain;

/// <summary>
/// Splits a markdown document into chunks, one per <c>##</c> section. Text before the first
/// section becomes an "Overview" chunk. The chunking rule is deliberately simple and
/// deterministic: it is easy to explain, and the same input always yields the same chunks (and
/// hashes), which is what lets ingestion skip anything that hasn't changed.
/// </summary>
public static class KnowledgeChunker
{
    public const string OverviewHeading = "Overview";

    public static List<KnowledgeChunkText> Chunk(KnowledgeDocument document)
    {
        var chunks = new List<KnowledgeChunkText>();
        var heading = OverviewHeading;
        var body = new List<string>();

        void Flush()
        {
            var content = string.Join("\n", body).Trim();
            if (content.Length > 0)
                chunks.Add(new KnowledgeChunkText(document.Id, document.Title, heading, content));
            body.Clear();
        }

        foreach (var line in document.Markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("## "))
            {
                Flush();
                heading = line[3..].Trim();
            }
            else if (line.StartsWith("# "))
            {
                // the document title line - already captured as document.Title
            }
            else
            {
                body.Add(line);
            }
        }
        Flush();

        return chunks;
    }
}
