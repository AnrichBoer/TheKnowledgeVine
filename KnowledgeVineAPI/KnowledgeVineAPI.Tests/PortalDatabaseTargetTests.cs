using KnowledgeVineAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeVineAPI.Tests;

public class PortalDatabaseTargetTests
{
    [Theory]
    [InlineData("GOTR_Live")]
    [InlineData("gotr_live")]
    [InlineData("postgres")]
    [InlineData("unrelated_database")]
    public void RejectsNonKnowledgeVineTargetsWithoutConnecting(string database) =>
        Assert.Throws<InvalidOperationException>(() =>
            PortalDatabaseTarget.Validate($"Host=127.0.0.1;Database={database}"));

    [Theory]
    [InlineData("Host=example.com;Database=knowledgevine_local")]
    [InlineData("Host=127.0.0.1;Database=knowledgevine_live")]
    public void LocalChecksCannotTargetRemoteOrProductionDatabases(string connection) =>
        Assert.Throws<InvalidOperationException>(() => PortalDatabaseTarget.Validate(connection, localOnly: true));

    [Fact]
    public void AcceptsTheExplicitLocalTarget() =>
        Assert.Equal("knowledgevine_local", PortalDatabaseTarget.Validate(
            "Host=127.0.0.1;Database=knowledgevine_local", localOnly: true).Database);

    [Fact]
    public void ModelContainsThePlannedSchoolAndIdentityTablesWithoutPasskeys()
    {
        using var db = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=knowledgevine_local").Options);
        Assert.Equal(new[] { "daily_updates", "parent_students", "role_claims", "roles", "students",
            "user_claims", "user_logins", "user_roles", "user_tokens", "users" },
            db.Model.GetEntityTypes().Select(x => x.GetTableName()).Order().ToArray());
    }
}
