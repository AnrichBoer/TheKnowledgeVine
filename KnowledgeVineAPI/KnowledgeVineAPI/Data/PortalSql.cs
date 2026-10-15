namespace KnowledgeVineAPI.Data;

public static class PortalSql
{
    public static string ReadResource(string filename)
    {
        using var stream = typeof(PortalSql).Assembly.GetManifestResourceStream($"KnowledgeVineAPI.Data.Sql.{filename}")
            ?? throw new InvalidOperationException($"Missing versioned portal SQL resource: {filename}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
