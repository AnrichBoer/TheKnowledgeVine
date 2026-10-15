using Npgsql;

namespace KnowledgeVineAPI.Data;

public static class PortalDatabaseTarget
{
    private static readonly HashSet<string> AllowedDatabases =
        ["knowledgevine_local", "knowledgevine", "knowledgevine_live"];

    public static NpgsqlConnectionStringBuilder Validate(string connectionString, bool localOnly = false)
    {
        NpgsqlConnectionStringBuilder target;
        try { target = new(connectionString); }
        catch (ArgumentException) { throw new InvalidOperationException("Invalid KnowledgeVine database configuration."); }

        if (!AllowedDatabases.Contains(target.Database ?? string.Empty))
            throw new InvalidOperationException("Database operations are restricted to explicitly named KnowledgeVine databases.");

        if (localOnly && (target.Database != "knowledgevine_local" ||
            target.Host is not ("127.0.0.1" or "localhost" or "::1")))
            throw new InvalidOperationException("Local setup and integration checks require the local KnowledgeVine database.");

        return target;
    }
}
