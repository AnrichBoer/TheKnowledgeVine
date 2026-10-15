using System.ComponentModel.DataAnnotations;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeVineAPI.Services.Portal;

public sealed record AdminAccount(Guid Id, string FirstName, string LastName, string Email, string[] Roles,
    bool IsActive, bool IsReady, string Version, Guid[] StudentIds);
public sealed record AdminStudent(Guid Id, string FirstName, string LastName, Guid? TeacherUserId,
    bool IsActive, DateTime UpdatedAt, Guid[] ParentIds);
public sealed record AdminDirectory(AdminAccount[] Accounts, AdminStudent[] Students, bool InvitationPreview);
public sealed record AccountInvitationResult(AdminAccount Account, string Delivery, string Message);
public sealed record CreateAccountRequest([Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName, [Required, EmailAddress, StringLength(256)] string Email,
    [Required] string Role, [Required, MaxLength(200)] Guid[] StudentIds);
public sealed record ParentLinksRequest([Required] string Version, [Required, MaxLength(200)] Guid[] StudentIds,
    [Required, MaxLength(200)] Guid[] ExpectedStudentIds);
public sealed record AccountStateRequest([Required] string Version, [Required] bool? IsActive);
public sealed record ResendInvitationRequest([Required] string Version);
public sealed record SaveStudentRequest([Required, StringLength(100)] string FirstName,
    [Required, StringLength(100)] string LastName, Guid? TeacherUserId, [Required] bool? IsActive,
    [Required, MaxLength(200)] Guid[] ParentIds, DateTime? ExpectedUpdatedAt,
    [Required, MaxLength(200)] Guid[] ExpectedParentIds);
public sealed class PortalAdminException(int status, string message, Guid? existingAccountId = null) : Exception(message)
{
    public int Status { get; } = status;
    public Guid? ExistingAccountId { get; } = existingAccountId;
}

public sealed class PortalAdminService(PortalDbContext db, UserManager<PortalUser> users,
    PortalAccountLinks links, PortalAccountPreviewMail previews, ILogger<PortalAdminService> logger)
{
    public async Task<AdminDirectory> DirectoryAsync(CancellationToken ct)
    {
        var accounts = await db.Users.AsNoTracking().OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ToArrayAsync(ct);
        var roles = await (from member in db.UserRoles join role in db.Roles on member.RoleId equals role.Id
                           select new { member.UserId, role.Name }).ToArrayAsync(ct);
        var students = await db.Students.AsNoTracking().OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ToArrayAsync(ct);
        var parents = await db.ParentStudents.AsNoTracking().ToArrayAsync(ct);
        return new(accounts.Select(u => new AdminAccount(u.Id, u.FirstName, u.LastName, u.Email!,
            roles.Where(r => r.UserId == u.Id).Select(r => r.Name!).Order().ToArray(), u.IsActive,
            u.EmailConfirmed && !string.IsNullOrEmpty(u.PasswordHash), u.ConcurrencyStamp ?? "",
            parents.Where(p => p.ParentUserId == u.Id).Select(p => p.StudentId).ToArray())).ToArray(),
            students.Select(s => new AdminStudent(s.Id, s.FirstName, s.LastName, s.TeacherUserId, s.IsActive,
                s.UpdatedAt, parents.Where(p => p.StudentId == s.Id).Select(p => p.ParentUserId).ToArray())).ToArray(),
            previews.InvitationPreviewEnabled);
    }

    public async Task<AccountInvitationResult> CreateAccountAsync(Guid admin, CreateAccountRequest request, CancellationToken ct)
    {
        Names(request.FirstName, request.LastName);
        if (request.Role is not (PortalRoles.Parent or PortalRoles.Teacher)) Fail(400, "Choose a Parent or Teacher account.");
        if (request.Role == PortalRoles.Teacher && request.StudentIds.Length > 0)
            Fail(400, "Teachers must complete password setup before students can be assigned.");
        var email = request.Email.Trim();
        var normalized = users.NormalizeEmail(email);
        var user = new PortalUser { FirstName = request.FirstName.Trim(), LastName = request.LastName.Trim(),
            Email = email, UserName = email, EmailConfirmed = false, IsActive = true };
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, ct);
            if (existing is not null) throw Duplicate(existing.Id);
            await LockStudents(request.StudentIds, ct);
            var created = await users.CreateAsync(user);
            if (!created.Succeeded && created.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, ct);
                if (existing is not null) throw Duplicate(existing.Id);
            }
            Require(created, "Please check the account's email and names.");
            Require(await users.AddToRoleAsync(user, request.Role), "The account role could not be saved.");
            foreach (var child in request.StudentIds.Distinct())
                db.ParentStudents.Add(new() { ParentUserId = user.Id, StudentId = child, LinkedByUserId = admin });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" })
        {
            db.ChangeTracker.Clear();
            var existing = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, ct);
            if (existing is not null) throw Duplicate(existing.Id);
            throw;
        }
        return await InviteAsync(user.Id, request.Role, false, null, ct);
    }

    public async Task<AccountInvitationResult> ResendAsync(Guid id, string version, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing("Account");
        var roles = await users.GetRolesAsync(user);
        var role = roles.Contains(PortalRoles.Teacher) ? PortalRoles.Teacher : roles.Contains(PortalRoles.Parent) ? PortalRoles.Parent : null;
        if (roles.Contains(PortalRoles.Admin) || role is null || !user.IsActive || !string.IsNullOrEmpty(user.PasswordHash))
            Fail(400, "Only active accounts awaiting password setup can receive another invitation.");
        return await InviteAsync(id, role!, true, version, ct);
    }

    private async Task<AccountInvitationResult> InviteAsync(Guid id, string role, bool resend, string? version, CancellationToken ct)
    {
        var delivery = previews.InvitationPreviewEnabled ? "preview" : "sent";
        var message = delivery == "preview" ? "Account saved. The test invitation is ready to preview; no email was sent."
            : "Account saved. Invitation accepted by the email server.";
        try { await links.SendInvitationAsync(id, role, resend, ct, version); }
        catch (PortalAdminException) { throw; }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("Portal invitation delivery failed ({ErrorType})", e.GetType().Name);
            delivery = "failed";
            message = "Account saved, but the invitation could not be delivered. Check email delivery and use Resend invitation.";
        }
        var directory = await DirectoryAsync(ct);
        return new(directory.Accounts.Single(a => a.Id == id), delivery, message);
    }

    public async Task<AdminAccount> SetAccountStateAsync(Guid id, AccountStateRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(id, ct);
        Version(user, request.Version);
        if (await users.IsInRoleAsync(user, PortalRoles.Admin)) Fail(400, "Administrator access cannot be changed from this screen.");
        user.IsActive = request.IsActive!.Value;
        // New security stamps revoke sessions and pending setup links, including after reactivation.
        Require(await users.UpdateSecurityStampAsync(user), "The account changed. Refresh before trying again.");
        if (!user.IsActive)
        {
            await db.Students.Where(s => s.TeacherUserId == id)
                .ExecuteUpdateAsync(update => update.SetProperty(s => s.TeacherUserId, (Guid?)null), ct);
        }
        await tx.CommitAsync(ct);
        return (await DirectoryAsync(ct)).Accounts.Single(a => a.Id == id);
    }

    public async Task<AdminAccount> SetParentLinksAsync(Guid admin, Guid id, ParentLinksRequest request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await LockUser(id, ct);
        Version(user, request.Version);
        if (!user.IsActive || !await users.IsInRoleAsync(user, PortalRoles.Parent)) Fail(400, "An active parent account is required.");
        var old = await db.ParentStudents.Where(p => p.ParentUserId == id).ToArrayAsync(ct);
        await LockStudents(request.StudentIds.Concat(old.Select(p => p.StudentId)).Distinct().ToArray(), ct);
        // Recheck after the child locks; student-side edits can remove a parent link.
        db.ChangeTracker.Clear();
        user = await LockUser(id, ct);
        Version(user, request.Version);
        old = await db.ParentStudents.Where(p => p.ParentUserId == id).ToArrayAsync(ct);
        if (!old.Select(p => p.StudentId).Order().SequenceEqual(request.ExpectedStudentIds.Distinct().Order()))
            Fail(409, "This parent's child links changed. Refresh before saving again.");
        db.ParentStudents.RemoveRange(old.Where(p => !request.StudentIds.Contains(p.StudentId)));
        foreach (var child in request.StudentIds.Distinct().Except(old.Select(p => p.StudentId)))
            db.ParentStudents.Add(new() { ParentUserId = id, StudentId = child, LinkedByUserId = admin });
        foreach (var child in old.Select(p => p.StudentId).Union(request.StudentIds))
        {
            var student = await db.Students.SingleAsync(s => s.Id == child, ct);
            db.Entry(student).Property(s => s.FirstName).IsModified = true;
        }
        Require(await users.UpdateAsync(user), "The account changed. Refresh before trying again.");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await DirectoryAsync(ct)).Accounts.Single(a => a.Id == id);
    }

    public async Task<AdminStudent> SaveStudentAsync(Guid admin, Guid? id, SaveStudentRequest request, CancellationToken ct)
    {
        Names(request.FirstName, request.LastName);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var selected = request.ParentIds.Concat(request.TeacherUserId.HasValue ? [request.TeacherUserId.Value] : Array.Empty<Guid>()).Distinct().Order();
        var locked = new Dictionary<Guid, PortalUser>();
        foreach (var selectedId in selected) locked[selectedId] = await LockUser(selectedId, ct);
        var student = id.HasValue ? await db.Students.FromSqlInterpolated($"SELECT * FROM students WHERE id = {id.Value} FOR UPDATE")
            .SingleOrDefaultAsync(ct) ?? throw Missing("Student") : new Student();
        if (id.HasValue && (!request.ExpectedUpdatedAt.HasValue || request.ExpectedUpdatedAt.Value.ToUniversalTime() != student.UpdatedAt))
            Fail(409, "This student changed in another window. Refresh before saving again.");
        if (request.TeacherUserId is Guid teacherId)
        {
            var teacher = locked[teacherId];
            if (!teacher.IsActive || !teacher.EmailConfirmed || string.IsNullOrEmpty(teacher.PasswordHash)
                || !await users.IsInRoleAsync(teacher, PortalRoles.Teacher))
                Fail(400, "Choose an active teacher who has completed password setup.");
        }
        var old = id.HasValue ? await db.ParentStudents.Where(p => p.StudentId == id).ToArrayAsync(ct) : [];
        if (id.HasValue && !old.Select(p => p.ParentUserId).Order().SequenceEqual(request.ExpectedParentIds.Distinct().Order()))
            Fail(409, "This student's parent links changed. Refresh before saving again.");
        foreach (var parentId in request.ParentIds.Distinct())
        {
            var parent = locked[parentId];
            if (!await users.IsInRoleAsync(parent, PortalRoles.Parent)
                || (!parent.IsActive && !old.Any(p => p.ParentUserId == parentId)))
                Fail(400, "New child links require an active parent account.");
        }
        student.FirstName = request.FirstName.Trim(); student.LastName = request.LastName.Trim();
        student.TeacherUserId = request.TeacherUserId; student.IsActive = request.IsActive!.Value;
        if (!id.HasValue) db.Students.Add(student);
        else db.Entry(student).Property(s => s.FirstName).IsModified = true;
        db.ParentStudents.RemoveRange(old.Where(p => !request.ParentIds.Contains(p.ParentUserId)));
        foreach (var parent in request.ParentIds.Distinct().Except(old.Select(p => p.ParentUserId)))
            db.ParentStudents.Add(new() { ParentUserId = parent, StudentId = student.Id, LinkedByUserId = admin });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await DirectoryAsync(ct)).Students.Single(s => s.Id == student.Id);
    }

    private async Task<PortalUser> LockUser(Guid id, CancellationToken ct)
        => await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
            ?? throw Missing("Account");
    private async Task LockStudents(Guid[] ids, CancellationToken ct)
    {
        foreach (var id in ids.Distinct().Order())
            if (await db.Students.FromSqlInterpolated($"SELECT * FROM students WHERE id = {id} FOR UPDATE").SingleOrDefaultAsync(ct) is null)
                throw Missing("Student");
    }
    private static void Names(string first, string last)
    { if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last)) Fail(400, "Enter a first name and last name."); }
    private static void Version(PortalUser user, string version)
    { if (user.ConcurrencyStamp != version) Fail(409, "This account changed in another window. Refresh before trying again."); }
    private static PortalAdminException Missing(string what) => new(404, $"{what} not found. Refresh and try again.");
    private static PortalAdminException Duplicate(Guid id) => new(409, "This email already has an account. Open the existing account to review its roles and links.", id);
    private static void Fail(int status, string message) => throw new PortalAdminException(status, message);
    private static void Require(IdentityResult result, string message)
    { if (!result.Succeeded) Fail(400, message); }
}
