using KnowledgeVineAPI.Models.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeVineAPI.Data;

public sealed record PortalAdminDetails(string Email, string FirstName, string LastName)
{
    public static PortalAdminDetails Default => new("anrich@knowledgevinemontessori.co.za", "Anrich", "Admin");
}

public enum PortalAdminBootstrapResult { Created, AlreadyExists }

public sealed class PortalAdminBootstrapException(string message) : Exception(message);

public sealed class PortalAdminBootstrap(PortalDbContext database, UserManager<PortalUser> users)
{
    public async Task<PortalAdminBootstrapResult> RunAsync(PortalAdminDetails details, Func<string> readPassword,
        CancellationToken cancellationToken = default)
    {
        PortalDatabaseTarget.Validate(database.Database.GetConnectionString()
            ?? throw new PortalAdminBootstrapException("KnowledgeVine database configuration is required."));
        if (string.IsNullOrWhiteSpace(details.Email) || string.IsNullOrWhiteSpace(details.FirstName)
            || string.IsNullOrWhiteSpace(details.LastName) || details.FirstName.Trim().Length > 100
            || details.LastName.Trim().Length > 100)
            throw new PortalAdminBootstrapException("An email and nonblank names of at most 100 characters are required.");
        if ((await database.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
            throw new PortalAdminBootstrapException("Apply the KnowledgeVine schema migrations before bootstrapping.");

        var existingResult = await CheckExistingAsync(details, cancellationToken);
        if (existingResult.HasValue) return existingResult.Value;
        // Human input must not hold a database transaction or lock open.
        var password = readPassword();
        if (string.IsNullOrEmpty(password))
            throw new PortalAdminBootstrapException("The initial admin password must not be empty.");

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Recheck under a database-scoped transaction lock to prevent concurrent first admins.
        await database.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(7410796189776889200)", cancellationToken);
        existingResult = await CheckExistingAsync(details, cancellationToken);
        if (existingResult.HasValue)
        {
            await transaction.CommitAsync(cancellationToken);
            return existingResult.Value;
        }

        var user = new PortalUser
        {
            Email = details.Email.Trim(), UserName = details.Email.Trim(),
            FirstName = details.FirstName.Trim(), LastName = details.LastName.Trim(),
            IsActive = true, EmailConfirmed = true // Trusted owner/operator bootstrap, not public registration.
        };
        RequireSuccess(await users.CreateAsync(user, password), "Account creation");
        RequireSuccess(await users.AddToRoleAsync(user, PortalRoles.Admin), "Admin role assignment");
        await database.Entry(user).ReloadAsync(cancellationToken);
        if (!await users.CheckPasswordAsync(user, password) || !await users.IsInRoleAsync(user, PortalRoles.Admin)
            || !user.EmailConfirmed || !user.IsActive)
            throw new PortalAdminBootstrapException("Created account verification failed; no account was committed.");
        await transaction.CommitAsync(cancellationToken);
        return PortalAdminBootstrapResult.Created;
    }

    private async Task<PortalAdminBootstrapResult?> CheckExistingAsync(PortalAdminDetails details, CancellationToken cancellationToken)
    {
        var normalizedEmail = users.NormalizeEmail(details.Email.Trim());
        var existing = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);
        var hasAdmin = await (from membership in database.UserRoles
                              join role in database.Roles on membership.RoleId equals role.Id
                              where role.NormalizedName == "ADMIN"
                              select membership.UserId).AnyAsync(cancellationToken);
        if (hasAdmin)
        {
            if (existing is not null && await users.IsInRoleAsync(existing, PortalRoles.Admin))
                return PortalAdminBootstrapResult.AlreadyExists;
            throw new PortalAdminBootstrapException("An admin already exists. Additional admins use account management, not bootstrap.");
        }
        if (existing is not null)
            throw new PortalAdminBootstrapException("That email already has an account; bootstrap will not promote or change it.");
        if (!await database.Roles.AnyAsync(x => x.NormalizedName == "ADMIN", cancellationToken))
            throw new PortalAdminBootstrapException("The seeded Admin role is missing. Apply the KnowledgeVine migrations.");

        return null;
    }

    private static void RequireSuccess(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new PortalAdminBootstrapException($"{operation} rejected by Identity: {string.Join(", ", result.Errors.Select(x => x.Code))}.");
    }
}
