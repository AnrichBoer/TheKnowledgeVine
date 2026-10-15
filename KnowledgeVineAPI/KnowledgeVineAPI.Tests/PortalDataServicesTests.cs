using KnowledgeVineAPI.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace KnowledgeVineAPI.Tests;

public class PortalDataServicesTests
{
    [Fact]
    public void DevelopmentRequiresLoopbackUnlessTheContainerSettingIsExplicit()
    {
        var configuration = Configuration("knowledgevine_local", allowContainer: false);
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddPortalData(configuration, new DevelopmentEnvironment()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainerSettingNeverAllowsTheClientDatabase(bool allowContainer)
    {
        var configuration = Configuration("GOTR_Live", allowContainer);
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddPortalData(configuration, new DevelopmentEnvironment()));
    }

    [Fact]
    public void ExplicitVmSettingAcceptsOnlyTheNamedKnowledgeVineDatabase()
    {
        var keys = Path.Combine(Path.GetTempPath(), "knowledgevine-vm-config-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var configuration = Configuration("knowledgevine_local", allowContainer: true, keys);
            var services = new ServiceCollection();
            services.AddLogging();
            Assert.True(services.AddPortalData(configuration, new DevelopmentEnvironment()));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Assert.Equal("knowledgevine_local", PortalDatabaseTarget.Validate(context.Database.GetConnectionString()!).Database);
        }
        finally { if (Directory.Exists(keys)) Directory.Delete(keys, recursive: true); }
    }

    private static IConfiguration Configuration(string database, bool allowContainer, string? keys = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:KnowledgeVine"] = $"Host=db;Database={database}",
            ["Portal:DataProtectionKeyPath"] = keys
        };
        if (allowContainer) values["Portal:LocalDatabaseOnly"] = "false";
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "KnowledgeVineTests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
