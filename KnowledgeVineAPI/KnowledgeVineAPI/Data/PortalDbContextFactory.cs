using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KnowledgeVineAPI.Data;

public sealed class PortalDbContextFactory : IDesignTimeDbContextFactory<PortalDbContext>
{
    public PortalDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<PortalDbContextFactory>(optional: true)
            .AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("KnowledgeVineMigrations")
            ?? configuration.GetConnectionString("KnowledgeVine")
            ?? "Host=127.0.0.1;Port=5432;Database=knowledgevine_local;Username=knowledgevine_migrator";
        PortalDatabaseTarget.Validate(connection);
        return new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseNpgsql(connection).Options);
    }
}
