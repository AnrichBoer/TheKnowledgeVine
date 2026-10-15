using System.Net;
using System.Net.Http.Json;
using KnowledgeVineAPI.Controllers;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace KnowledgeVineAPI.Tests;

public class PortalAuthHttpTests
{
    private const string Password = "Fictional-Portal-Password-42!";
    private const string ChangedPassword = "Fictional-Changed-Password-73!";

    [PortalBootstrapFact]
    public async Task AnonymousAccessAndUnprotectedMutationsAreRejected()
    {
        await using var test = await AuthFixture.Create();
        var response = await test.Client.GetAsync("/portal/auth/session");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync("/portal/access/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/auth/login",
            new { email = test.Admin.Email, password = Password })).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task LoginUsesASessionCookieAndEnforcesRolesAndLogout()
    {
        await using var test = await AuthFixture.Create();
        var login = await test.Login(test.Admin);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var session = await login.Content.ReadFromJsonAsync<PortalSession>();
        Assert.Equal(new[] { "Admin" }, session!.Roles);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("KnowledgeVine.Portal="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("max-age=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.True(login.Headers.CacheControl?.NoStore);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.GetAsync("/portal/access/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.GetAsync("/portal/access/teacher")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.GetAsync("/portal/access/parent")).StatusCode);
        await test.Csrf();
        Assert.Equal(HttpStatusCode.NoContent, (await test.Client.PostAsJsonAsync("/portal/auth/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync("/portal/auth/session")).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task TeacherParentAndMultipleRoleAccountsHaveOnlyTheirPermittedViews()
    {
        await using var test = await AuthFixture.Create();
        Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.GetAsync("/portal/access/teacher")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.GetAsync("/portal/access/admin")).StatusCode);
        await test.SignOut();
        Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Parent)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.GetAsync("/portal/access/parent")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.GetAsync("/portal/access/teacher")).StatusCode);
        await test.SignOut();
        await test.ChangeUser(test.Teacher.Id, async (users, user) => Assert.True((await users.AddToRoleAsync(user, "Parent")).Succeeded));
        Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.GetAsync("/portal/access/teacher")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.GetAsync("/portal/access/parent")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.GetAsync("/portal/access/admin")).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task BackgroundReadsDoNotRenewTheThirtyMinuteSession()
    {
        await using var test = await AuthFixture.Create();
        var session = await (await test.Login(test.Admin)).Content.ReadFromJsonAsync<PortalSession>();
        test.Factory.Clock.Advance(TimeSpan.FromMinutes(29));
        var stillActive = await test.Client.GetFromJsonAsync<PortalSession>("/portal/auth/session");
        // Cookie tickets serialize expiry at second precision.
        Assert.Equal(session!.ExpiresAt.ToUnixTimeSeconds(), stillActive!.ExpiresAt.ToUnixTimeSeconds());
        test.Factory.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync("/portal/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.PostAsJsonAsync("/portal/auth/activity", new { })).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task ExplicitActivityRenewsTheIdleWindow()
    {
        await using var test = await AuthFixture.Create();
        await test.Login(test.Admin);
        await test.Csrf();
        test.Factory.Clock.Advance(TimeSpan.FromMinutes(20));
        var renewed = await test.Client.PostAsJsonAsync("/portal/auth/activity", new { });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        test.Factory.Clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(HttpStatusCode.OK, (await test.Client.GetAsync("/portal/auth/session")).StatusCode);
        test.Factory.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync("/portal/auth/session")).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task DeactivationAndRoleChangesInvalidateExistingCookies()
    {
        await using var test = await AuthFixture.Create();
        await test.Login(test.Teacher);
        await test.ChangeUser(test.Teacher.Id, async (users, user) =>
        { user.IsActive = false; Assert.True((await users.UpdateAsync(user)).Succeeded); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync("/portal/access/teacher")).StatusCode);
        await test.Login(test.Parent);
        await test.ChangeUser(test.Parent.Id, async (users, user) => Assert.True((await users.RemoveFromRoleAsync(user, "Parent")).Succeeded));
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync("/portal/auth/session")).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task SetupLinksAreReplacedAndConsumedWithoutAutomaticallyLoggingIn()
    {
        await using var test = await AuthFixture.Create();
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Login(test.Pending)).StatusCode);
        await test.Recover(test.Pending.Email!);
        var first = test.Factory.Mail.Messages.Last();
        await test.Recover(test.Pending.Email!);
        var second = test.Factory.Mail.Messages.Last();
        Assert.True(second.InitialSetup);
        Assert.Contains('#', second.Link.AbsoluteUri);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Complete(first, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Complete(second, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync("/portal/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Complete(second, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Pending, ChangedPassword)).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task ExpiredSetupLinksAreRejectedWithoutCompletingTheAccount()
    {
        await using var test = await AuthFixture.Create(expireSetup: true);
        await test.Recover(test.Pending.Email!);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Complete(test.Factory.Mail.Messages.Last(), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Login(test.Pending)).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task PasswordResetConsumesTheLinkAndInvalidatesOldCookies()
    {
        await using var test = await AuthFixture.Create();
        var login = await test.Login(test.Teacher);
        var originalCookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("KnowledgeVine.Portal=")).Split(';')[0];
        await test.Recover(test.Teacher.Email!);
        var message = test.Factory.Mail.Messages.Last();
        Assert.False(message.InitialSetup);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.GetAsync("/portal/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Complete(message, false)).StatusCode);
        using var oldSession = test.Factory.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
        oldSession.DefaultRequestHeaders.Add("Cookie", originalCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/portal/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Complete(message, false)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Login(test.Teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Teacher, ChangedPassword)).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task WrongAccountAndTamperedTokensCannotResetAPassword()
    {
        await using var test = await AuthFixture.Create();
        await test.Recover(test.Teacher.Email!);
        var query = QueryHelpers.ParseQuery(test.Factory.Mail.Messages.Last().Link.Fragment[1..]);
        await test.Csrf();
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/auth/reset-password",
            new { userId = test.Parent.Id, token = query["token"].ToString(), password = ChangedPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/auth/reset-password",
            new { userId = test.Teacher.Id, token = "broken-link", password = ChangedPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Teacher)).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task RecoveryDoesNotDiscloseAccountsAndReportsUnavailableDelivery()
    {
        await using var test = await AuthFixture.Create();
        var known = await test.Recover(test.Teacher.Email!);
        var unknown = await test.Recover("unknown@example.test");
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Single(test.Factory.Mail.Messages);
        test.Factory.Mail.Available = false;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await test.Recover(test.Teacher.Email!)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await test.Recover("unknown@example.test")).StatusCode);
        test.Factory.Mail.Available = true; test.Factory.Mail.Fail = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await test.Recover(test.Teacher.Email!)).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task RepeatedWrongPasswordsLockTheAccountAndRecoveryRestoresAccess()
    {
        await using var test = await AuthFixture.Create();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await test.Login(test.Teacher, "Wrong-Fictional-Password-11!")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Login(test.Teacher)).StatusCode);
        await test.Recover(test.Teacher.Email!);
        Assert.Equal(HttpStatusCode.OK, (await test.Complete(test.Factory.Mail.Messages.Last(), false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Teacher, ChangedPassword)).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task InvitationServicesUseRegisteredRolesAndReplaceResentLinks()
    {
        await using var test = await AuthFixture.Create();
        await using (var scope = test.Factory.Services.CreateAsyncScope())
        {
            var links = scope.ServiceProvider.GetRequiredService<PortalAccountLinks>();
            await links.SendInvitationAsync(test.Pending.Id, "Teacher", false, CancellationToken.None);
            var first = test.Factory.Mail.Messages.Last();
            Assert.Equal(test.Pending.Email, first.Recipient);
            Assert.Equal("Teacher", first.Role);
            Assert.Equal("Fictional", first.FirstName);
            Assert.False(first.IsResend);
            await links.SendInvitationAsync(test.Pending.Id, "Teacher", true, CancellationToken.None);
            Assert.True(test.Factory.Mail.Messages.Last().IsResend);
            Assert.Equal(HttpStatusCode.BadRequest, (await test.Complete(first, true)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, (await test.Complete(test.Factory.Mail.Messages.Last(), true)).StatusCode);
        await using var completed = test.Factory.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => completed.ServiceProvider.GetRequiredService<PortalAccountLinks>()
            .SendInvitationAsync(test.Pending.Id, "Teacher", true, CancellationToken.None));
    }

    [PortalBootstrapFact]
    public async Task FailedInvitationDeliveryPreservesThePendingAccountForRetry()
    {
        await using var test = await AuthFixture.Create();
        test.Factory.Mail.Fail = true;
        await using (var scope = test.Factory.Services.CreateAsyncScope())
        {
            var links = scope.ServiceProvider.GetRequiredService<PortalAccountLinks>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => links.SendInvitationAsync(test.Pending.Id, "Teacher", false, CancellationToken.None));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            var pending = (await users.FindByIdAsync(test.Pending.Id.ToString()))!;
            Assert.True(pending.IsActive);
            Assert.False(pending.EmailConfirmed);
            Assert.Null(pending.PasswordHash);
            Assert.True(await users.IsInRoleAsync(pending, "Teacher"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => links.SendInvitationAsync(pending.Id, "Parent", false, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => links.SendInvitationAsync(pending.Id, "Admin", false, CancellationToken.None));
        }
        test.Factory.Mail.Fail = false;
        Assert.Equal(HttpStatusCode.OK, (await test.Recover(test.Pending.Email!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Complete(test.Factory.Mail.Messages.Last(), true)).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task ExpiredResetLinksCannotChangeTheExistingPassword()
    {
        await using var test = await AuthFixture.Create(expireReset: true);
        await test.Recover(test.Teacher.Email!);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Complete(test.Factory.Mail.Messages.Last(), false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Teacher)).StatusCode);
    }

    internal sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }

    internal sealed class CapturingMail : IPortalAccountMail
    {
        public bool Available { get; set; } = true;
        public bool Fail { get; set; }
        public bool IsAvailable => Available;
        public List<PortalAccountMessage> Messages { get; } = [];
        public Task SendAsync(PortalAccountMessage message, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("Fictional delivery failure.");
            Messages.Add(message); return Task.CompletedTask;
        }
    }

    internal sealed class AuthFactory(NpgsqlConnection connection, bool expireSetup, bool expireReset,
        string keyPath, Clock clock) : WebApplicationFactory<Program>
    {
        public Clock Clock { get; } = clock;
        public CapturingMail Mail { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            var root = FindProject();
            builder.UseContentRoot(root);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:KnowledgeVine"] = connection.ConnectionString,
                ["Portal:DataProtectionKeyPath"] = keyPath,
                ["Portal:PublicUrl"] = "http://localhost:4200"
            }));
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<PortalDbContext>>();
                services.AddDbContext<PortalDbContext>(options => options.UseNpgsql(connection));
                services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(Clock);
                services.RemoveAll<IPortalAccountMail>(); services.AddSingleton<IPortalAccountMail>(Mail);
                services.RemoveAll<IDataProtectionProvider>();
                services.AddSingleton<IDataProtectionProvider>(DataProtectionProvider.Create(new DirectoryInfo(keyPath),
                    protection => protection.SetApplicationName("KnowledgeVinePortalTests")));
                if (expireSetup) services.Configure<PortalSetupTokenOptions>(options => options.TokenLifespan = TimeSpan.FromSeconds(-1));
                if (expireReset) services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromSeconds(-1));
            });
        }

        private static string FindProject()
        {
            var configuredRoot = Environment.GetEnvironmentVariable("KNOWLEDGEVINE_PROJECT_ROOT");
            if (!string.IsNullOrWhiteSpace(configuredRoot) && File.Exists(Path.Combine(configuredRoot, "KnowledgeVineAPI/KnowledgeVineAPI/KnowledgeVineAPI.csproj")))
                return Path.Combine(configuredRoot, "KnowledgeVineAPI/KnowledgeVineAPI");
            var current = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (current is not null)
            {
                var project = Path.Combine(current.FullName, "KnowledgeVineAPI/KnowledgeVineAPI/KnowledgeVineAPI.csproj");
                if (File.Exists(project)) return Path.GetDirectoryName(project)!;
                current = current.Parent;
            }
            throw new InvalidOperationException("KnowledgeVine project root was not found.");
        }
    }

    internal sealed class AuthFixture : IAsyncDisposable
    {
        private readonly NpgsqlConnection connection;
        private readonly bool expireSetup;
        private readonly bool expireReset;
        private readonly string keyPath = Path.Combine(Path.GetTempPath(), "knowledge-vine-auth-tests-" + Guid.NewGuid().ToString("N"));
        public AuthFactory Factory { get; private set; }
        public HttpClient Client { get; private set; }
        public PortalUser Admin { get; private set; } = null!;
        public PortalUser Teacher { get; private set; } = null!;
        public PortalUser Parent { get; private set; } = null!;
        public PortalUser Pending { get; private set; } = null!;
        private AuthFixture(NpgsqlConnection connection, bool expireSetup, bool expireReset)
        {
            this.connection = connection;
            this.expireSetup = expireSetup;
            this.expireReset = expireReset;
            Factory = new(connection, expireSetup, expireReset, keyPath, new Clock());
            Client = Factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        }

        // Replace the actual HTTP host, DI container and DbContexts while keeping PostgreSQL rows
        // and the persisted key directory. This exercises restart compatibility without real fixtures.
        public async Task RestartApi()
        {
            var clock = Factory.Clock;
            Client.Dispose();
            await Factory.DisposeAsync();
            Factory = new(connection, expireSetup, expireReset, keyPath, clock);
            Client = Factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        }

        public static async Task<AuthFixture> Create(bool expireSetup = false, bool expireReset = false)
        {
            var target = Environment.GetEnvironmentVariable("KNOWLEDGEVINE_BOOTSTRAP_TEST_CONNECTION")!;
            var options = PortalDatabaseTarget.Validate(target, localOnly: true);
            options.Pooling = false;
            var connection = new NpgsqlConnection(options.ConnectionString);
            await connection.OpenAsync();
            foreach (var table in new[] { "users", "roles", "user_roles", "user_claims", "role_claims", "user_logins", "user_tokens", "students", "parent_students", "daily_updates" })
            {
                await using var command = new NpgsqlCommand($"CREATE TEMP TABLE {table} (LIKE public.{table} INCLUDING ALL)", connection);
                await command.ExecuteNonQueryAsync();
            }
            foreach (var table in new[] { "users", "students" })
            {
                await using var stamp = new NpgsqlCommand($"CREATE TRIGGER stamp BEFORE UPDATE ON pg_temp.{table} FOR EACH ROW EXECUTE FUNCTION public.portal_stamp_updated_at()", connection);
                await stamp.ExecuteNonQueryAsync();
            }
            await using var seed = new NpgsqlCommand("INSERT INTO pg_temp.roles SELECT * FROM public.roles", connection);
            await seed.ExecuteNonQueryAsync();
            // Exercise the released note triggers against session-local school/Identity rows.
            // Never replace public functions or persist fictional HTTP-test accounts.
            var rules = PortalSql.ReadResource("portal_rules_v1.sql");
            foreach (System.Text.RegularExpressions.Match function in System.Text.RegularExpressions.Regex.Matches(rules,
                @"CREATE FUNCTION public\.(portal_user_has_role|portal_user_is_ready|portal_teacher_is_ready|portal_check_daily_update)\([\s\S]*?\n\$\$;"))
            {
                await using var create = new NpgsqlCommand(function.Value.Replace("public.", "pg_temp."), connection);
                await create.ExecuteNonQueryAsync();
            }
            await using var noteTrigger = new NpgsqlCommand("CREATE TRIGGER check_note BEFORE INSERT OR UPDATE ON pg_temp.daily_updates FOR EACH ROW EXECUTE FUNCTION pg_temp.portal_check_daily_update()", connection);
            await noteTrigger.ExecuteNonQueryAsync();
            var fixture = new AuthFixture(connection, expireSetup, expireReset);
            await using var scope = fixture.Factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            fixture.Admin = await CreateUser(users, "Admin");
            fixture.Teacher = await CreateUser(users, "Teacher");
            fixture.Parent = await CreateUser(users, "Parent");
            fixture.Pending = await CreateUser(users, "Teacher", pending: true);
            return fixture;
        }

        private static async Task<PortalUser> CreateUser(UserManager<PortalUser> users, string role, bool pending = false)
        {
            var email = Guid.NewGuid().ToString("N") + "@example.test";
            var user = new PortalUser { Email = email, UserName = email, FirstName = "Fictional", LastName = role,
                EmailConfirmed = !pending, IsActive = true };
            var result = pending ? await users.CreateAsync(user) : await users.CreateAsync(user, Password);
            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(x => x.Code)));
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            return user;
        }

        public async Task Csrf()
        {
            var response = await Client.GetAsync("/portal/auth/csrf");
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var cookie = response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN="));
            var token = Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);
            Client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
            Client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
        }

        public async Task<HttpResponseMessage> Login(PortalUser user, string password = Password)
        {
            await Csrf();
            return await Client.PostAsJsonAsync("/portal/auth/login", new { email = user.Email, password });
        }
        public async Task<HttpResponseMessage> Recover(string email)
        {
            await Csrf();
            return await Client.PostAsJsonAsync("/portal/auth/forgot-password", new { email });
        }
        public async Task<HttpResponseMessage> Complete(PortalAccountMessage message, bool initial)
        {
            await Csrf();
            var values = QueryHelpers.ParseQuery(message.Link.Fragment[1..]);
            return await Client.PostAsJsonAsync(initial ? "/portal/auth/setup-password" : "/portal/auth/reset-password",
                new { userId = values["userId"].ToString(), token = values["token"].ToString(), password = ChangedPassword });
        }
        public async Task SignOut()
        {
            await Csrf();
            Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsJsonAsync("/portal/auth/logout", new { })).StatusCode);
        }
        public async Task ChangeUser(Guid id, Func<UserManager<PortalUser>, PortalUser, Task> change)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            await change(users, (await users.FindByIdAsync(id.ToString()))!);
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); await Factory.DisposeAsync(); await connection.DisposeAsync();
            if (Directory.Exists(keyPath)) Directory.Delete(keyPath, recursive: true);
        }
    }
}
