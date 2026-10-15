using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace KnowledgeVineAPI.Tests;

public sealed class PortalBootstrapFactAttribute : FactAttribute
{
    public PortalBootstrapFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KNOWLEDGEVINE_BOOTSTRAP_TEST_CONNECTION")))
            Skip = "Run scripts/test_portal_database.py for isolated local Identity bootstrap checks.";
    }
}

public sealed class PortalBootstrapTheoryAttribute : TheoryAttribute
{
    public PortalBootstrapTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KNOWLEDGEVINE_BOOTSTRAP_TEST_CONNECTION")))
            Skip = "Run scripts/test_portal_database.py for isolated local Identity checks.";
    }
}

public class PortalAdminBootstrapTests
{
    private const string TestPassword = "Fictional-Admin-Setup-42!";
    private static PortalAdminDetails Details => new("bootstrap@example.test", "Fictional", "Admin");

    [Fact]
    public void CommandDefaultsToTheChosenInitialAdmin()
        => Assert.Equal("anrich@knowledgevinemontessori.co.za",
            PortalAdminBootstrapCommand.Parse(["--bootstrap-admin"]).Email);

    [Theory]
    [InlineData("--password")]
    [InlineData("--connection-string")]
    [InlineData("--unexpected")]
    public void PasswordArgumentsAndUnknownOptionsAreRejected(string option)
        => Assert.Throws<PortalAdminBootstrapException>(() =>
            PortalAdminBootstrapCommand.Parse(["--bootstrap-admin", option, "value"]));

    [Fact]
    public async Task NonKnowledgeVineTargetIsRejectedBeforeAnyDatabaseCall()
    {
        using var context = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=GOTR_Live").Options);
        var bootstrap = new PortalAdminBootstrap(context, null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bootstrap.RunAsync(Details,
            () => throw new InvalidOperationException("Password must not be requested.")));
    }

    [PortalBootstrapFact]
    public async Task CreatesAReadyAdminWithAnIdentityHashAndPreservesItOnRerun()
    {
        await using var test = await BootstrapFixture.Create();
        Assert.Equal(PortalAdminBootstrapResult.Created, await test.Bootstrap.RunAsync(Details, () =>
        {
            Assert.Null(test.Context.Database.CurrentTransaction);
            return TestPassword;
        }));
        var user = await test.Context.Users.SingleAsync();
        Assert.True(user.IsActive && user.EmailConfirmed);
        Assert.NotEqual(TestPassword, user.PasswordHash);
        Assert.True(await test.Users.CheckPasswordAsync(user, TestPassword));
        Assert.True(await test.Users.IsInRoleAsync(user, PortalRoles.Admin));
        var before = (user.Id, user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp, user.UpdatedAt);
        Assert.Equal(PortalAdminBootstrapResult.AlreadyExists,
            await test.Bootstrap.RunAsync(Details with { Email = Details.Email.ToUpperInvariant(), FirstName = "Changed" },
                () => throw new InvalidOperationException("Reruns must not read a password.")));
        await test.Context.Entry(user).ReloadAsync();
        Assert.Equal(before, (user.Id, user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp, user.UpdatedAt));
        Assert.Equal("Fictional", user.FirstName);
        Assert.Equal(1, await test.Context.Users.CountAsync());
        Assert.Equal(1, await test.Context.UserRoles.CountAsync());
    }

    [PortalBootstrapFact]
    public async Task RerunDoesNotUndoLaterPasswordChangesOrReactivateAnAccount()
    {
        await using var test = await BootstrapFixture.Create();
        await test.Bootstrap.RunAsync(Details, () => TestPassword);
        var user = await test.Context.Users.SingleAsync();
        const string changedPassword = "Fictional-Changed-Password-73!";
        Assert.True((await test.Users.ChangePasswordAsync(user, TestPassword, changedPassword)).Succeeded);
        user.IsActive = false;
        await test.Context.SaveChangesAsync();
        var stamp = user.SecurityStamp;
        await test.Bootstrap.RunAsync(Details, () => TestPassword);
        await test.Context.Entry(user).ReloadAsync();
        Assert.False(user.IsActive);
        Assert.Equal(stamp, user.SecurityStamp);
        Assert.True(await test.Users.CheckPasswordAsync(user, changedPassword));
        Assert.False(await test.Users.CheckPasswordAsync(user, TestPassword));
    }

    [PortalBootstrapFact]
    public async Task BootstrapCannotCreateAnotherAdminWhenOneAlreadyExists()
    {
        await using var test = await BootstrapFixture.Create();
        await test.Bootstrap.RunAsync(Details, () => TestPassword);
        await Assert.ThrowsAsync<PortalAdminBootstrapException>(() => test.Bootstrap.RunAsync(
            Details with { Email = "second-admin@example.test" },
            () => throw new InvalidOperationException("Must refuse before requesting a password.")));
        Assert.Equal(1, await test.Context.Users.CountAsync());
    }

    [PortalBootstrapFact]
    public async Task BootstrapNeverPromotesAnExistingTeacherAccount()
    {
        await using var test = await BootstrapFixture.Create();
        var user = new PortalUser { Email = Details.Email, UserName = Details.Email,
            FirstName = "Fictional", LastName = "Teacher", EmailConfirmed = true };
        Assert.True((await test.Users.CreateAsync(user, TestPassword)).Succeeded);
        Assert.True((await test.Users.AddToRoleAsync(user, PortalRoles.Teacher)).Succeeded);
        await Assert.ThrowsAsync<PortalAdminBootstrapException>(() => test.Bootstrap.RunAsync(Details,
            () => throw new InvalidOperationException("Must refuse before requesting a password.")));
        Assert.False(await test.Users.IsInRoleAsync(user, PortalRoles.Admin));
        Assert.True(await test.Users.IsInRoleAsync(user, PortalRoles.Teacher));
        Assert.Equal(1, await test.Context.Users.CountAsync());
    }

    [PortalBootstrapFact]
    public async Task InvalidPasswordDoesNotLeaveAnAccountOrRoleGrant()
    {
        await using var test = await BootstrapFixture.Create();
        var error = await Assert.ThrowsAsync<PortalAdminBootstrapException>(() => test.Bootstrap.RunAsync(Details, () => "weak"));
        Assert.Contains("Identity", error.Message);
        Assert.Equal(0, await test.Context.Users.CountAsync());
        Assert.Equal(0, await test.Context.UserRoles.CountAsync());
    }

    [PortalBootstrapFact]
    public async Task MissingAdminRoleDoesNotCreateAPartialAccount()
    {
        await using var test = await BootstrapFixture.Create();
        await test.Context.Roles.Where(x => x.Id == PortalRoles.AdminId).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<PortalAdminBootstrapException>(() => test.Bootstrap.RunAsync(Details,
            () => throw new InvalidOperationException("Must refuse before requesting a password.")));
        Assert.Equal(0, await test.Context.Users.CountAsync());
    }

    [PortalBootstrapFact]
    public async Task AFailedRoleGrantRollsBackTheNewAccount()
    {
        await using var test = await BootstrapFixture.Create();
        await test.Context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE pg_temp.user_roles ADD CONSTRAINT bootstrap_test_reject_roles CHECK (false)");
        await Assert.ThrowsAsync<DbUpdateException>(() => test.Bootstrap.RunAsync(Details, () => TestPassword));
        Assert.Equal(0, await test.Context.Users.CountAsync());
        Assert.Equal(0, await test.Context.UserRoles.CountAsync());
    }

    [PortalBootstrapFact]
    public async Task AnAdminAppearingDuringPasswordEntryBlocksASecondAdmin()
    {
        await using var test = await BootstrapFixture.Create();
        var other = new PortalUser
        {
            Email = "competing-admin@example.test", UserName = "competing-admin@example.test",
            NormalizedEmail = "COMPETING-ADMIN@EXAMPLE.TEST", NormalizedUserName = "COMPETING-ADMIN@EXAMPLE.TEST",
            FirstName = "Competing", LastName = "Admin", EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        other.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(other, TestPassword);
        await Assert.ThrowsAsync<PortalAdminBootstrapException>(() => test.Bootstrap.RunAsync(Details, () =>
        {
            Assert.Null(test.Context.Database.CurrentTransaction);
            test.Context.Users.Add(other);
            test.Context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = other.Id, RoleId = PortalRoles.AdminId });
            test.Context.SaveChanges(); // Model a competing setup completing while this operator types.
            return TestPassword;
        }));
        Assert.Equal(other.Id, (await test.Context.Users.SingleAsync()).Id);
        Assert.Equal(1, await test.Context.UserRoles.CountAsync());
    }

    private sealed class BootstrapFixture : IAsyncDisposable
    {
        private readonly NpgsqlConnection connection;
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;
        public PortalDbContext Context { get; }
        public UserManager<PortalUser> Users { get; }
        public PortalAdminBootstrap Bootstrap { get; }

        private BootstrapFixture(NpgsqlConnection connection, ServiceProvider provider, AsyncServiceScope scope)
        {
            this.connection = connection;
            this.provider = provider;
            this.scope = scope;
            Context = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            Users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            Bootstrap = scope.ServiceProvider.GetRequiredService<PortalAdminBootstrap>();
        }

        public static async Task<BootstrapFixture> Create()
        {
            var target = Environment.GetEnvironmentVariable("KNOWLEDGEVINE_BOOTSTRAP_TEST_CONNECTION")!;
            var settings = PortalDatabaseTarget.Validate(target, localOnly: true);
            settings.Pooling = false;
            var connection = new NpgsqlConnection(settings.ConnectionString);
            await connection.OpenAsync();
            try
            {
                // Session-local tables shadow Identity tables. No working user records are modified,
                // and all fictional data disappears when this non-pooled connection closes.
                foreach (var table in new[] { "users", "roles", "user_roles", "user_claims", "role_claims", "user_logins", "user_tokens" })
                {
                    await using var command = new NpgsqlCommand($"CREATE TEMP TABLE {table} (LIKE public.{table} INCLUDING ALL)", connection);
                    await command.ExecuteNonQueryAsync();
                }
                await using var seed = new NpgsqlCommand("INSERT INTO pg_temp.roles SELECT * FROM public.roles", connection);
                await seed.ExecuteNonQueryAsync();
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddDbContext<PortalDbContext>(options => options.UseNpgsql(connection));
                services.AddIdentityCore<PortalUser>(PortalDataServices.ConfigureIdentityOptions)
                    .AddRoles<IdentityRole<Guid>>().AddEntityFrameworkStores<PortalDbContext>();
                services.AddScoped<PortalAdminBootstrap>();
                var provider = services.BuildServiceProvider();
                var scope = provider.CreateAsyncScope();
                return new BootstrapFixture(connection, provider, scope);
            }
            catch { await connection.DisposeAsync(); throw; }
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
