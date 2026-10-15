using KnowledgeVineAPI.Models.Portal;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeVineAPI.Tests;

public class PortalPostgresTests
{
    [PortalPostgresFact]
    public async Task TeacherMustCompleteSetupBeforeAssignment()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.ExpectFailure("23514", () => Assign(db, db.PendingTeacher.Id));
        await db.ExpectFailure("23514", () => Assign(db, db.Parent.Id));
        await db.ExpectFailure("42501", () => db.Scalar<long>(
            "SELECT count(*) FROM public.portal_teacher_students(@teacher)", ("teacher", db.PendingTeacher.Id)));

        PortalPostgresFixture.CompleteSetup(db.PendingTeacher);
        await db.Context.SaveChangesAsync();
        await Assign(db, db.PendingTeacher.Id);
        Assert.Equal(db.PendingTeacher.Id, await db.Scalar<Guid>(
            "SELECT teacher_user_id FROM public.students WHERE id = @student", ("student", db.Child.Id)));
        Assert.Equal(1, await db.SaveNote(db.Child.Id, "Ready teacher can write.", teacher: db.PendingTeacher.Id));
    }

    [PortalPostgresFact]
    public async Task PendingParentCanBeLinkedButCannotReadUntilSetupIsComplete()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.Link(db.PendingParent.Id, db.Child.Id);
        await db.SaveNote(db.Child.Id, "A shared update.", share: true);
        await db.ExpectFailure("42501", () => db.FeedCount(db.PendingParent.Id));
        PortalPostgresFixture.CompleteSetup(db.PendingParent);
        await db.Context.SaveChangesAsync();
        Assert.Equal(1, await db.FeedCount(db.PendingParent.Id));
    }

    [PortalPostgresFact]
    public async Task ParentFeedExcludesDraftsAndUnlinkedChildrenAndSupportsSiblings()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.SaveNote(db.Child.Id, "Private draft.");
        await db.SaveNote(db.OtherChild.Id, "Another family's update.", share: true);
        Assert.Equal(0, await db.FeedCount(db.Parent.Id));
        await db.ExpectFailure("42501", () => db.Scalar<long>(
            "SELECT count(*) FROM public.portal_parent_updates(@parent, @student)",
            ("parent", db.Parent.Id), ("student", db.OtherChild.Id)));

        await db.SaveNote(db.Child.Id, "Shared with this child's parents.", share: true, version: 1);
        await db.SaveNote(db.Sibling.Id, "Sibling's update.", share: true);
        Assert.Equal(2, await db.FeedCount(db.Parent.Id));
        Assert.Equal(1, await db.FeedCount(db.OtherParent.Id));
        await db.Link(db.OtherParent.Id, db.Child.Id);
        await db.Link(db.OtherParent.Id, db.Child.Id); // Resubmitting the same grant is harmless.
        Assert.Equal(2, await db.FeedCount(db.OtherParent.Id));
        await db.Execute("SELECT public.portal_unlink_parent(@admin, @student, @parent)",
            ("admin", db.Admin.Id), ("student", db.Child.Id), ("parent", db.OtherParent.Id));
        Assert.Equal(1, await db.FeedCount(db.OtherParent.Id));
        await db.ExpectFailure("42501", () => db.FeedCount(db.Teacher.Id));
    }

    [PortalPostgresTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n ")]
    public async Task BlankSharedNotesAreRejectedButEmptyDraftsAreAllowed(string text)
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.ExpectFailure("23514", () => db.SaveNote(db.Child.Id, text, share: true));
        Assert.Equal(1, await db.SaveNote(db.Child.Id, text));
        Assert.Equal(0, await db.FeedCount(db.Parent.Id));
    }

    [PortalPostgresFact]
    public async Task NoteLengthLimitIsEnforcedByPostgres()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.ExpectFailure("23514", () => db.SaveNote(db.Child.Id, new string('x', 2001)));
        Assert.Equal(1, await db.SaveNote(db.Child.Id, new string('x', 2000), share: true));
    }

    [PortalPostgresFact]
    public async Task OnlyTheActiveAssignedTeacherCanWriteForAnActiveStudent()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.ExpectFailure("42501", () => db.SaveNote(db.OtherTeacherChild.Id, "Wrong teacher."));
        await db.ExpectFailure("42501", () => db.SaveNote(db.Child.Id, "Wrong role.", teacher: db.Admin.Id));
        await db.ExpectFailure("42501", () => db.SaveNote(db.Child.Id, "Not ready.", teacher: db.PendingTeacher.Id));
        db.Teacher.IsActive = false;
        await db.Context.SaveChangesAsync();
        await db.ExpectFailure("42501", () => db.SaveNote(db.Child.Id, "Deactivated teacher."));
        db.Teacher.IsActive = true;
        db.Child.IsActive = false;
        await db.Context.SaveChangesAsync();
        await db.ExpectFailure("42501", () => db.SaveNote(db.Child.Id, "Inactive student."));
    }

    [PortalPostgresFact]
    public async Task CorrectionsKeepTheOriginalAuthorAndFirstShareTime()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.SaveNote(db.Child.Id, "Initial note.", share: true);
        var original = await db.Context.DailyUpdates.AsNoTracking().SingleAsync(x => x.StudentId == db.Child.Id);
        Assert.Equal(original.SharedAt, original.UpdatedAt);
        await Assign(db, db.OtherTeacher.Id);
        Assert.Equal(2, await db.SaveNote(db.Child.Id, "Corrected note.", version: 1, teacher: db.OtherTeacher.Id));
        var edited = await db.Context.DailyUpdates.AsNoTracking().SingleAsync(x => x.StudentId == db.Child.Id);
        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(original.CreatedAt, edited.CreatedAt);
        Assert.Equal(original.CreatedByUserId, edited.CreatedByUserId);
        Assert.Equal(original.SharedAt, edited.SharedAt);
        Assert.Equal(db.OtherTeacher.Id, edited.UpdatedByUserId);
        Assert.Equal(DailyUpdateStatuses.Shared, edited.Status);
        Assert.True(edited.UpdatedAt > edited.SharedAt);
        Assert.Equal("OtherTeacher Integration", await db.Scalar<string>(
            "SELECT edited_by_name FROM public.portal_parent_updates(@parent, @student)",
            ("parent", db.Parent.Id), ("student", db.Child.Id)));
        await db.ExpectFailure("42501", () => db.SaveNote(db.Child.Id, "Old teacher.", version: 2));
    }

    [PortalPostgresFact]
    public async Task StaleOrMissingVersionsCannotOverwriteANote()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.SaveNote(db.Child.Id, "First draft.");
        await db.SaveNote(db.Child.Id, "Newer draft.", version: 1);
        await db.ExpectFailure("40001", () => db.SaveNote(db.Child.Id, "Stale draft.", version: 1));
        await db.ExpectFailure("40001", () => db.SaveNote(db.Child.Id, "No version."));
        await db.ExpectFailure("40001", () => db.SaveNote(db.Sibling.Id, "No existing draft.", version: 1));
        Assert.Equal("Newer draft.", await db.Scalar<string>(
            "SELECT note_text FROM public.daily_updates WHERE student_id = @student", ("student", db.Child.Id)));
    }

    [PortalPostgresFact]
    public async Task EfCoreChecksConcurrencyAndRetrievesTriggerGeneratedValues()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.SaveNote(db.Child.Id, "Initial draft.");
        await using var first = await db.AnotherContext();
        await using var second = await db.AnotherContext();
        var a = await first.DailyUpdates.SingleAsync(x => x.StudentId == db.Child.Id);
        var b = await second.DailyUpdates.SingleAsync(x => x.StudentId == db.Child.Id);
        a.NoteText = "Shared from EF.";
        a.Status = DailyUpdateStatuses.Shared;
        a.UpdatedByUserId = db.Teacher.Id;
        await first.SaveChangesAsync();
        Assert.Equal(2, a.Version);
        Assert.NotNull(a.SharedAt);
        Assert.Equal(a.SharedAt, a.UpdatedAt);
        b.NoteText = "Would overwrite a newer note.";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.Equal("Shared from EF.", await db.Scalar<string>(
            "SELECT note_text FROM public.daily_updates WHERE student_id = @student", ("student", db.Child.Id)));
    }

    [PortalPostgresFact]
    public async Task OneDailyNoteCannotBeDuplicatedMovedOrUnshared()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.SaveNote(db.Child.Id, "Shared note.", share: true);
        await db.ExpectFailure("23505", () => db.Execute("""
            INSERT INTO public.daily_updates(id, student_id, update_date, note_text, status, created_by_user_id, updated_by_user_id)
            VALUES(gen_random_uuid(), @student, @date, 'Duplicate', 'draft', @teacher, @teacher)
            """, ("student", db.Child.Id), ("date", db.SchoolDate), ("teacher", db.Teacher.Id)));
        await db.ExpectFailure("23514", () => db.Execute(
            "UPDATE public.daily_updates SET status = 'draft' WHERE student_id = @student", ("student", db.Child.Id)));
        await db.ExpectFailure("23514", () => db.Execute(
            "UPDATE public.daily_updates SET student_id = @sibling WHERE student_id = @student",
            ("sibling", db.Sibling.Id), ("student", db.Child.Id)));
        await db.ExpectFailure("23514", () => db.Execute(
            "UPDATE public.daily_updates SET update_date = update_date + 1 WHERE student_id = @student", ("student", db.Child.Id)));
        await db.ExpectFailure("23514", () => db.Execute(
            "UPDATE public.daily_updates SET created_by_user_id = @other WHERE student_id = @student",
            ("other", db.OtherTeacher.Id), ("student", db.Child.Id)));
    }

    [PortalPostgresFact]
    public async Task LinkingAndAssigningRequireAnAdminAndTheCorrectAccountRole()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.ExpectFailure("42501", () => db.Execute(
            "SELECT public.portal_assign_teacher(@actor, @student, @teacher)",
            ("actor", db.Parent.Id), ("student", db.Child.Id), ("teacher", db.OtherTeacher.Id)));
        await db.ExpectFailure("42501", () => db.Execute(
            "SELECT public.portal_link_parent(@actor, @student, @parent)",
            ("actor", db.Teacher.Id), ("student", db.OtherChild.Id), ("parent", db.Parent.Id)));
        await db.ExpectFailure("42501", () => db.Execute(
            "SELECT public.portal_unlink_parent(@actor, @student, @parent)",
            ("actor", db.Teacher.Id), ("student", db.Child.Id), ("parent", db.Parent.Id)));
        await db.ExpectFailure("23514", () => db.Link(db.Teacher.Id, db.Child.Id));
        await db.ExpectFailure("23514", () => db.Execute(
            "UPDATE public.parent_students SET linked_by_user_id = @admin WHERE student_id = @student",
            ("admin", db.Admin.Id), ("student", db.Child.Id)));
        await db.ExpectFailure("23503", () => db.Link(db.Parent.Id, Guid.NewGuid()));
    }

    [PortalPostgresFact]
    public async Task DeactivatedParentImmediatelyLosesFeedAccess()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.SaveNote(db.Child.Id, "Visible while active.", share: true);
        Assert.Equal(1, await db.FeedCount(db.Parent.Id));
        db.Parent.IsActive = false;
        await db.Context.SaveChangesAsync();
        await db.ExpectFailure("42501", () => db.FeedCount(db.Parent.Id));
    }

    [PortalPostgresFact]
    public async Task RosterShowsOnlyAssignedActiveStudentsWithDateSpecificStatus()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.SaveNote(db.Child.Id, "Draft.");
        await db.SaveNote(db.Sibling.Id, "Shared.", share: true);
        Assert.Equal(3, await db.Scalar<long>(
            "SELECT count(*) FROM public.portal_teacher_students(@teacher, @date)",
            ("teacher", db.Teacher.Id), ("date", db.SchoolDate)));
        Assert.Equal("draft", await RosterStatus(db, db.Child.Id, db.SchoolDate));
        Assert.Equal("shared", await RosterStatus(db, db.Sibling.Id, db.SchoolDate));
        Assert.Equal("not_started", await RosterStatus(db, db.Child.Id, db.SchoolDate.AddDays(1)));
        db.OtherChild.IsActive = false;
        await db.Context.SaveChangesAsync();
        Assert.Equal(2, await db.Scalar<long>(
            "SELECT count(*) FROM public.portal_teacher_students(@teacher)", ("teacher", db.Teacher.Id)));
    }

    [PortalPostgresFact]
    public async Task FeedIsNewestFirstAndRejectsInvalidPagination()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.SaveNote(db.Child.Id, "Yesterday.", share: true, date: new DateOnly(db.SchoolDate.Year, 6, 14));
        await db.SaveNote(db.Child.Id, "Today.", share: true, date: new DateOnly(db.SchoolDate.Year, 6, 15));
        Assert.Equal("Today.", await db.Scalar<string>(
            "SELECT note_text FROM public.portal_parent_updates(@parent, @student, 1, 0)",
            ("parent", db.Parent.Id), ("student", db.Child.Id)));
        Assert.Equal("Yesterday.", await db.Scalar<string>(
            "SELECT note_text FROM public.portal_parent_updates(@parent, @student, 1, 1)",
            ("parent", db.Parent.Id), ("student", db.Child.Id)));
        await db.ExpectFailure("22023", () => db.Scalar<long>(
            "SELECT count(*) FROM public.portal_parent_updates(@parent, NULL, 101, 0)", ("parent", db.Parent.Id)));
        await db.ExpectFailure("22023", () => db.Scalar<long>(
            "SELECT count(*) FROM public.portal_parent_updates(@parent, NULL, 50, -1)", ("parent", db.Parent.Id)));
    }

    [PortalPostgresFact]
    public async Task ForeignKeysProtectReferencedAccountsAndStudentHistory()
    {
        await using var db = await PortalPostgresFixture.Create();
        await db.SaveNote(db.Child.Id, "Keep the history.", share: true);
        await db.ExpectFailure("23001", () => db.Execute(
            "DELETE FROM public.students WHERE id = @id", ("id", db.Child.Id)));
        await db.ExpectFailure("23001", () => db.Execute(
            "DELETE FROM public.users WHERE id = @id", ("id", db.Teacher.Id)));
        await db.ExpectFailure("23001", () => db.Execute(
            "DELETE FROM public.users WHERE id = @id", ("id", db.Parent.Id)));
    }

    [PortalPostgresFact]
    public async Task RuntimeRoleCannotChangeTheSchemaOrMigrationHistory()
    {
        await using var db = await PortalPostgresFixture.Create();
        Assert.Equal("knowledgevine_app", await db.Scalar<string>("SELECT current_user::text"));
        Assert.False(await db.Scalar<bool>("SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.False(await db.Scalar<bool>("SELECT has_schema_privilege(current_user, 'public', 'CREATE')"));
        Assert.False(await db.Scalar<bool>("SELECT has_table_privilege(current_user, 'public.\"__EFMigrationsHistory\"', 'UPDATE')"));
        Assert.Equal(0, await db.Scalar<long>("""
            SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = 'public' AND p.proname LIKE 'portal_%' AND p.prosecdef
            """));
        await db.ExpectFailure("42501", () => db.Execute("CREATE TABLE public.portal_permission_probe(id integer)"));
    }

    private static Task Assign(PortalPostgresFixture db, Guid teacher) => db.Execute(
        "SELECT public.portal_assign_teacher(@admin, @student, @teacher)",
        ("admin", db.Admin.Id), ("student", db.Child.Id), ("teacher", teacher));

    private static Task<string> RosterStatus(PortalPostgresFixture db, Guid student, DateOnly date) => db.Scalar<string>(
        "SELECT status FROM public.portal_teacher_students(@teacher, @date) WHERE student_id = @student",
        ("teacher", db.Teacher.Id), ("date", date), ("student", student));
}
