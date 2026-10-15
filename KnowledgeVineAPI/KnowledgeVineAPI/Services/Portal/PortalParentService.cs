using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeVineAPI.Services.Portal;

public sealed record ParentChild(Guid Id, string FirstName, string LastName);
public sealed record ParentChildren(int Year, DateTimeOffset ServerTime, DateTimeOffset NextYearAt, ParentChild[] Children);
public sealed record ParentNote(Guid Id, DateOnly UpdateDate, string NoteText, string AuthorName,
    string EditorName, DateTime SharedAt, DateTime UpdatedAt, bool IsEdited);
public sealed record ParentFeed(int Year, DateTimeOffset ServerTime, DateTimeOffset NextYearAt,
    ParentChild Child, ParentNote[] Notes, DateOnly? NextBefore);
public sealed class PortalParentException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class PortalParentService(PortalDbContext db, TimeProvider clock)
{
    private static readonly TimeZoneInfo SchoolZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    private (int Year, DateTimeOffset Now, DateTimeOffset NextYear) Window()
    {
        var now = clock.GetUtcNow();
        var year = TimeZoneInfo.ConvertTime(now, SchoolZone).Year;
        var next = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return (year, now, new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(next, SchoolZone)));
    }

    // Keep readiness, enrollment and the family link inside every data query. A cookie
    // or a previously loaded child selector never grants access to a later read.
    private IQueryable<Student> Children(Guid parent) =>
        from s in db.Students.AsNoTracking()
        join link in db.ParentStudents on s.Id equals link.StudentId
        join user in db.Users on link.ParentUserId equals user.Id
        where link.ParentUserId == parent && s.IsActive && user.IsActive && user.EmailConfirmed
            && user.PasswordHash != null && user.PasswordHash != ""
            && (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                where ur.UserId == parent && r.NormalizedName == "PARENT" select ur).Any()
        select s;

    public async Task<ParentChildren> ChildrenAsync(Guid parent, CancellationToken ct)
    {
        var window = Window();
        var children = await Children(parent).OrderBy(s => s.FirstName).ThenBy(s => s.LastName).ThenBy(s => s.Id)
            .Select(s => new ParentChild(s.Id, s.FirstName, s.LastName)).ToArrayAsync(ct);
        return new(window.Year, window.Now, window.NextYear, children);
    }

    private IQueryable<DailyUpdate> Notes(Guid parent, Guid child, int year) =>
        db.DailyUpdates.AsNoTracking().Where(note => note.StudentId == child
            && Children(parent).Any(s => s.Id == note.StudentId)
            && note.Status == DailyUpdateStatuses.Shared && note.SharedAt != null
            && note.UpdateDate >= new DateOnly(year, 1, 1) && note.UpdateDate < new DateOnly(year + 1, 1, 1));

    private IQueryable<ParentNote> ReadNotes(IQueryable<DailyUpdate> notes) =>
        from note in notes
        join author in db.Users on note.CreatedByUserId equals author.Id
        join editor in db.Users on note.UpdatedByUserId equals editor.Id
        select new ParentNote(note.Id, note.UpdateDate, note.NoteText, author.FirstName + " " + author.LastName,
            editor.FirstName + " " + editor.LastName, note.SharedAt!.Value, note.UpdatedAt, note.UpdatedAt > note.SharedAt);

    public async Task<ParentFeed> FeedAsync(Guid parent, Guid childId, DateOnly? before, int limit, CancellationToken ct)
    {
        var window = Window();
        if (limit is < 1 or > 50 || (before.HasValue && (before.Value < new DateOnly(window.Year, 1, 1)
            || before.Value > new DateOnly(window.Year + 1, 1, 1))))
            throw new PortalParentException(400, "Choose a valid page of this year's updates.");
        var child = await Children(parent).Where(s => s.Id == childId)
            .Select(s => new ParentChild(s.Id, s.FirstName, s.LastName)).SingleOrDefaultAsync(ct) ?? throw Unavailable();
        var query = Notes(parent, childId, window.Year);
        if (before.HasValue) query = query.Where(n => n.UpdateDate < before.Value);
        // There is one note per child/date, so the school date is a stable page cursor.
        var page = await ReadNotes(query.OrderByDescending(n => n.UpdateDate).Take(limit + 1)).ToArrayAsync(ct);
        page = page.OrderByDescending(n => n.UpdateDate).ToArray();
        return new(window.Year, window.Now, window.NextYear, child, page.Take(limit).ToArray(),
            page.Length > limit ? page[limit - 1].UpdateDate : null);
    }

    public async Task<ParentNote> NoteAsync(Guid parent, Guid child, Guid note, CancellationToken ct)
        => await ReadNotes(Notes(parent, child, Window().Year).Where(n => n.Id == note)).SingleOrDefaultAsync(ct) ?? throw Unavailable();

    private static PortalParentException Unavailable() => new(404, "This update or child is no longer available to your account. Refresh your children's list or contact the school.");
}
