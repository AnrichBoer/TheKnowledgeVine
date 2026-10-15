using System.Text;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KnowledgeVineAPI.Services.Portal;

public sealed class PortalSetupTokenOptions : DataProtectionTokenProviderOptions
{
    public PortalSetupTokenOptions() { Name = "KnowledgeVine.InitialSetup"; TokenLifespan = TimeSpan.FromHours(24); }
}

public sealed class PortalSetupTokenProvider(IDataProtectionProvider protection, IOptions<PortalSetupTokenOptions> options,
    ILogger<DataProtectorTokenProvider<PortalUser>> logger)
    : DataProtectorTokenProvider<PortalUser>(protection, options, logger);

public sealed class PortalAccountLinks(PortalDbContext database, UserManager<PortalUser> users,
    IPortalAccountMail mail, PortalMailTemplates templates)
{
    public const string SetupProvider = "PortalSetup";
    public const string SetupPurpose = "InitialPasswordSetup";

    public bool DeliveryAvailable => mail.IsAvailable;

    public async Task RequestRecoveryAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || !user.IsActive || !await HasPortalRole(user)) return;
        var initial = string.IsNullOrEmpty(user.PasswordHash);
        if (!initial && !user.EmailConfirmed) return;
        var origin = templates.PublicOrigin(); // Validate configuration before replacing an invitation.
        var token = initial ? await GenerateSetupTokenAsync(user) : await users.GeneratePasswordResetTokenAsync(user);
        var roles = await users.GetRolesAsync(user);
        var role = roles.Contains(PortalRoles.Teacher) ? PortalRoles.Teacher : roles.Contains(PortalRoles.Parent) ? PortalRoles.Parent : null;
        await SendAsync(user, origin, token, initial, role, initial, cancellationToken);
    }

    // Admin account creation/resend screens in step 4 call this after committing the account and links.
    public async Task SendInvitationAsync(Guid userId, string role, bool resend, CancellationToken cancellationToken, string? expectedVersion = null)
    {
        if (role is not (PortalRoles.Parent or PortalRoles.Teacher))
            throw new InvalidOperationException("Invitations require a Parent or Teacher role.");
        if (!mail.IsAvailable) throw new InvalidOperationException("Portal account email delivery is unavailable.");
        var origin = templates.PublicOrigin();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await database.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (user is not null) await database.Entry(user).ReloadAsync(cancellationToken);
        if (expectedVersion is not null && user?.ConcurrencyStamp != expectedVersion)
            throw new PortalAdminException(409, "This account changed. Refresh before resending the invitation.");
        if (user is null || !user.IsActive || !string.IsNullOrEmpty(user.PasswordHash) || !await users.IsInRoleAsync(user, role))
            throw new InvalidOperationException("An active pending account with the requested role is required.");
        var token = await GenerateSetupTokenAsync(user);
        // Persist replacement before SMTP; delivery failure must still invalidate the old link.
        await transaction.CommitAsync(cancellationToken);
        await SendAsync(user, origin, token, true, role, resend, cancellationToken);
    }

    private async Task SendAsync(PortalUser user, Uri origin, string token, bool initial, string? role, bool resend,
        CancellationToken cancellationToken)
    {
        var path = initial ? "/portal/setup-password" : "/portal/reset-password";
        // Fragments never reach HTTP access logs or referrer headers.
        var fragment = QueryString.Create(new Dictionary<string, string?>
            { ["userId"] = user.Id.ToString(), ["token"] = Encode(token) }).Value!.TrimStart('?');
        var url = new Uri(origin, path).AbsoluteUri + "#" + fragment;
        await mail.SendAsync(new(user.Email!, new Uri(url), initial, user.FirstName, role, resend), cancellationToken);
    }

    public async Task<string> GenerateSetupTokenAsync(PortalUser user)
    {
        if (!user.IsActive || !string.IsNullOrEmpty(user.PasswordHash) || !await HasPortalRole(user))
            throw new InvalidOperationException("A pending active portal account is required.");
        var result = await users.UpdateSecurityStampAsync(user); // Resends replace previous pending invitations.
        if (!result.Succeeded) throw new InvalidOperationException("Could not replace the pending setup link.");
        return await users.GenerateUserTokenAsync(user, SetupProvider, SetupPurpose);
    }

    public async Task<IdentityResult> SetPasswordAsync(Guid userId, string encodedToken, string password, bool initial,
        CancellationToken cancellationToken)
    {
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encodedToken)); }
        catch (Exception error) when (error is FormatException or ArgumentException) { return InvalidLink(); }
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        // Serialize completion so two requests cannot both consume the same setup/reset token.
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM users WHERE id = {userId} FOR UPDATE", cancellationToken);
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return InvalidLink();
        await database.Entry(user).ReloadAsync(cancellationToken);
        if (!user.IsActive || !await HasPortalRole(user)) return InvalidLink();
        IdentityResult result;
        if (initial)
        {
            if (!string.IsNullOrEmpty(user.PasswordHash) ||
                !await users.VerifyUserTokenAsync(user, SetupProvider, SetupPurpose, token)) return InvalidLink();
            user.EmailConfirmed = true;
            result = await users.AddPasswordAsync(user, password);
        }
        else
        {
            if (string.IsNullOrEmpty(user.PasswordHash) || !user.EmailConfirmed) return InvalidLink();
            result = await users.ResetPasswordAsync(user, token, password);
        }
        if (!result.Succeeded) return result;
        if (user.LockoutEnabled)
        {
            result = await users.ResetAccessFailedCountAsync(user);
            if (!result.Succeeded) return result;
            result = await users.SetLockoutEndDateAsync(user, null);
            if (!result.Succeeded) return result;
        }
        await transaction.CommitAsync(cancellationToken);
        return IdentityResult.Success;
    }

    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private async Task<bool> HasPortalRole(PortalUser user) => (await users.GetRolesAsync(user)).Intersect(PortalAuthentication.Roles).Any();
    private static IdentityResult InvalidLink() => IdentityResult.Failed(new IdentityError { Code = "InvalidToken", Description = "This link is invalid, expired or already used. Request a new link." });
}
