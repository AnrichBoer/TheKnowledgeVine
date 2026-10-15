using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace KnowledgeVineAPI.Tests;

public sealed class PortalPostgresFactAttribute : FactAttribute
{
    public PortalPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KNOWLEDGEVINE_TEST_CONNECTION")))
            Skip = "Run scripts/test_portal_database.py for explicitly scoped local PostgreSQL checks.";
    }
}

public sealed class PortalPostgresTheoryAttribute : TheoryAttribute
{
    public PortalPostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KNOWLEDGEVINE_TEST_CONNECTION")))
            Skip = "Run scripts/test_portal_database.py for explicitly scoped local PostgreSQL checks.";
    }
}

internal sealed class PortalPostgresFixture : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; }
    public NpgsqlTransaction Transaction { get; }
    public PortalDbContext Context { get; }
    public PortalUser Admin { get; private set; } = null!;
    public PortalUser Teacher { get; private set; } = null!;
    public PortalUser OtherTeacher { get; private set; } = null!;
    public PortalUser Parent { get; private set; } = null!;
    public PortalUser OtherParent { get; private set; } = null!;
    public PortalUser PendingTeacher { get; private set; } = null!;
    public PortalUser PendingParent { get; private set; } = null!;
    public Student Child { get; private set; } = null!;
    public Student Sibling { get; private set; } = null!;
    public Student OtherChild { get; private set; } = null!;
    public Student OtherTeacherChild { get; private set; } = null!;
    public DateOnly SchoolDate { get; private set; }

    private PortalPostgresFixture(NpgsqlConnection connection, NpgsqlTransaction transaction, PortalDbContext context)
        => (Connection, Transaction, Context) = (connection, transaction, context);

    public static async Task<PortalPostgresFixture> Create()
    {
        var connectionString = Environment.GetEnvironmentVariable("KNOWLEDGEVINE_TEST_CONNECTION")
            ?? throw new InvalidOperationException("Explicit local test connection required.");
        PortalDatabaseTarget.Validate(connectionString, localOnly: true);
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        Assert.Equal("knowledgevine_local", connection.Database);
        var transaction = await connection.BeginTransactionAsync();
        var context = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseNpgsql(connection).Options);
        await context.Database.UseTransactionAsync(transaction);
        var fixture = new PortalPostgresFixture(connection, transaction, context);
        try
        {
            fixture.SchoolDate = await fixture.Scalar<DateOnly>("SELECT (timezone('Africa/Johannesburg', CURRENT_TIMESTAMP))::date");
            fixture.Admin = fixture.NewUser(PortalRoles.Admin, PortalRoles.AdminId);
            fixture.Teacher = fixture.NewUser(PortalRoles.Teacher, PortalRoles.TeacherId);
            fixture.OtherTeacher = fixture.NewUser("OtherTeacher", PortalRoles.TeacherId);
            fixture.Parent = fixture.NewUser(PortalRoles.Parent, PortalRoles.ParentId);
            fixture.OtherParent = fixture.NewUser("OtherParent", PortalRoles.ParentId);
            fixture.PendingTeacher = fixture.NewUser("PendingTeacher", PortalRoles.TeacherId, ready: false);
            fixture.PendingParent = fixture.NewUser("PendingParent", PortalRoles.ParentId, ready: false);
            await context.SaveChangesAsync();
            fixture.Child = fixture.NewStudent("Child", fixture.Teacher.Id);
            fixture.Sibling = fixture.NewStudent("Sibling", fixture.Teacher.Id);
            fixture.OtherChild = fixture.NewStudent("OtherChild", fixture.Teacher.Id);
            fixture.OtherTeacherChild = fixture.NewStudent("OtherTeacherChild", fixture.OtherTeacher.Id);
            await context.SaveChangesAsync();
            await fixture.Link(fixture.Parent.Id, fixture.Child.Id);
            await fixture.Link(fixture.Parent.Id, fixture.Sibling.Id);
            await fixture.Link(fixture.OtherParent.Id, fixture.OtherChild.Id);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    private PortalUser NewUser(string name, Guid role, bool ready = true)
    {
        var user = new PortalUser
        {
            FirstName = name, LastName = "Integration",
            Email = $"{Guid.NewGuid():N}@example.test", IsActive = true, EmailConfirmed = ready,
            SecurityStamp = Guid.NewGuid().ToString(), ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        user.UserName = user.Email;
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        user.NormalizedUserName = user.NormalizedEmail;
        if (ready) CompleteSetup(user);
        Context.Users.Add(user);
        Context.UserRoles.Add(new IdentityUserRole<Guid> { UserId = user.Id, RoleId = role });
        return user;
    }

    public static void CompleteSetup(PortalUser user)
    {
        user.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(user, "Fictional-Test-Password-42!");
        user.EmailConfirmed = true;
    }

    private Student NewStudent(string firstName, Guid teacher)
    {
        var student = new Student { FirstName = firstName, LastName = "Integration", TeacherUserId = teacher };
        Context.Students.Add(student);
        return student;
    }

    public NpgsqlCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, Connection, Transaction);
        foreach (var (name, value) in parameters)
            command.Parameters.Add(value is null
                ? new NpgsqlParameter(name, NpgsqlDbType.Integer) { Value = DBNull.Value }
                : new NpgsqlParameter { ParameterName = name, Value = value });
        return command;
    }

    public async Task<T> Scalar<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        return (T)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Expected a scalar result."));
    }

    public async Task Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    public Task Link(Guid parent, Guid student) => Execute(
        "SELECT public.portal_link_parent(@admin, @student, @parent)",
        ("admin", Admin.Id), ("student", student), ("parent", parent));

    public Task<int> SaveNote(Guid child, string text, bool share = false, int? version = null, Guid? teacher = null,
        DateOnly? date = null) => Scalar<int>(
        "SELECT version FROM public.portal_save_daily_update(@teacher, @student, @date, @note, @share, @version)",
        ("teacher", teacher ?? Teacher.Id), ("student", child), ("date", date ?? SchoolDate),
        ("note", text), ("share", share), ("version", version));

    public Task<long> FeedCount(Guid parent) => Scalar<long>(
        "SELECT count(*) FROM public.portal_parent_updates(@parent)", ("parent", parent));

    public async Task ExpectFailure(string state, Func<Task> operation)
    {
        await Transaction.SaveAsync("expected_failure");
        try
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(operation);
            Assert.Equal(state, exception.SqlState);
        }
        finally
        {
            await Transaction.RollbackAsync("expected_failure");
            await Transaction.ReleaseAsync("expected_failure");
        }
    }

    public async Task<PortalDbContext> AnotherContext()
    {
        var context = new PortalDbContext(new DbContextOptionsBuilder<PortalDbContext>().UseNpgsql(Connection).Options);
        await context.Database.UseTransactionAsync(Transaction);
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await Transaction.RollbackAsync();
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}
