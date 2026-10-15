using System.ComponentModel.DataAnnotations;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KnowledgeVineAPI.Services.Portal;

public sealed record TeacherStudent(Guid Id, string FirstName, string LastName, string Status, int? Version);
public sealed record TeacherWorklist(DateOnly Today, DateOnly Date, TeacherStudent[] Students);
public sealed record TeacherNote(Guid Id, string NoteText, string Status, int Version, string AuthorName,
    string EditorName, DateTime CreatedAt, DateTime UpdatedAt, DateTime? SharedAt);
public sealed record TeacherEditor(DateOnly Today, DateOnly Date, TeacherStudent Student, TeacherNote? Note);
public sealed record SaveTeacherNoteRequest([Required] DateOnly? UpdateDate,
    [Required(AllowEmptyStrings = true), StringLength(2000)] string NoteText,
    [Required] bool? Share, [Range(1, int.MaxValue)] int? ExpectedVersion);
public sealed class PortalTeacherException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class PortalTeacherService(PortalDbContext db, TimeProvider clock)
{
    private static readonly TimeZoneInfo SchoolZone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Johannesburg");
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), SchoolZone).DateTime);

    private DateOnly SchoolDate(DateOnly? date)
    {
        var selected = date ?? Today;
        if (selected > Today) throw new PortalTeacherException(400, "Choose today or an earlier school date.");
        return selected;
    }

    public async Task<TeacherWorklist> WorklistAsync(Guid teacher, DateOnly? date, CancellationToken ct)
    {
        var selected = SchoolDate(date);
        var students = await (from s in db.Students.AsNoTracking()
            where s.IsActive && s.TeacherUserId == teacher
            join n in db.DailyUpdates.Where(n => n.UpdateDate == selected) on s.Id equals n.StudentId into notes
            from n in notes.DefaultIfEmpty()
            orderby s.LastName, s.FirstName, s.Id
            select new TeacherStudent(s.Id, s.FirstName, s.LastName, n == null ? "not_started" : n.Status,
                n == null ? null : (int?)n.Version)).ToArrayAsync(ct);
        return new(Today, selected, students);
    }

    public async Task<TeacherEditor> EditorAsync(Guid teacher, Guid studentId, DateOnly? date, CancellationToken ct)
    {
        var selected = SchoolDate(date);
        var student = await db.Students.AsNoTracking().SingleOrDefaultAsync(s => s.Id == studentId
            && s.IsActive && s.TeacherUserId == teacher, ct) ?? throw Unavailable();
        var note = await (from n in db.DailyUpdates.AsNoTracking()
            join author in db.Users on n.CreatedByUserId equals author.Id
            join editor in db.Users on n.UpdatedByUserId equals editor.Id
            where n.StudentId == studentId && n.UpdateDate == selected
            select new TeacherNote(n.Id, n.NoteText, n.Status, n.Version, author.FirstName + " " + author.LastName,
                editor.FirstName + " " + editor.LastName, n.CreatedAt, n.UpdatedAt, n.SharedAt)).SingleOrDefaultAsync(ct);
        return new(Today, selected, new(student.Id, student.FirstName, student.LastName,
            note?.Status ?? "not_started", note?.Version), note);
    }

    public async Task<TeacherEditor> SaveAsync(Guid teacher, Guid studentId, SaveTeacherNoteRequest input, CancellationToken ct)
    {
        var selected = SchoolDate(input.UpdateDate);
        if (input.UpdateDate is null || input.Share is null || input.NoteText is null || input.NoteText.Length > 2000
            || input.ExpectedVersion is <= 0) throw new PortalTeacherException(400, "Check the date and note (maximum 2,000 characters).");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Use the same user-before-student lock order as account deactivation. Assignment,
            // enrollment and another first save cannot change beneath this write.
            var actor = await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE id={teacher} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (actor is not { IsActive: true, EmailConfirmed: true } || string.IsNullOrEmpty(actor.PasswordHash)
                || !await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id
                    where ur.UserId == teacher && r.NormalizedName == "TEACHER" select ur).AnyAsync(ct)) throw Unavailable();
            var student = await db.Students.FromSqlInterpolated($"SELECT * FROM students WHERE id={studentId} FOR UPDATE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (student is null || !student.IsActive || student.TeacherUserId != teacher) throw Unavailable();
            var note = await db.DailyUpdates.SingleOrDefaultAsync(n => n.StudentId == studentId && n.UpdateDate == selected, ct);
            if (note?.Version != input.ExpectedVersion) throw Conflict();
            var shared = input.Share.Value || note?.Status == DailyUpdateStatuses.Shared;
            if (shared && string.IsNullOrWhiteSpace(input.NoteText))
                throw new PortalTeacherException(400, "Write a note before sharing it with parents.");
            if (note is null)
            {
                note = new() { StudentId = studentId, UpdateDate = selected, CreatedByUserId = teacher };
                db.DailyUpdates.Add(note);
            }
            note.NoteText = input.NoteText;
            note.Status = shared ? DailyUpdateStatuses.Shared : DailyUpdateStatuses.Draft;
            note.UpdatedByUserId = teacher;
            // Existing database triggers preserve original authorship/first share and generate
            // timestamps/version. The EF model retrieves these generated values on every save.
            await db.SaveChangesAsync(ct);
            var result = await EditorAsync(teacher, studentId, selected, ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException p)
        {
            throw p.SqlState switch
            {
                "23505" or "40001" => Conflict(),
                "42501" => Unavailable(),
                "23514" => new PortalTeacherException(400, "Check the note and try again. Shared notes must contain text and remain shared."),
                _ => e
            };
        }
    }

    private static PortalTeacherException Unavailable() => new(404, "This student is no longer available in your assigned list. Your unsaved text has not been submitted.");
    private static PortalTeacherException Conflict() => new(409, "This note changed in another window. Your text is kept. Compare the latest saved note before saving again.");
}
