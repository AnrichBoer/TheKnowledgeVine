using System.Net;
using System.Net.Http.Json;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static KnowledgeVineAPI.Tests.PortalAuthHttpTests;

namespace KnowledgeVineAPI.Tests;

public class PortalTeacherHttpTests
{
    private static DateOnly Today(AuthFixture test) => DateOnly.FromDateTime(test.Factory.Clock.GetUtcNow().AddHours(2).DateTime);
    private static string Url(Guid id) => $"/portal/teacher/students/{id}/note";
    private static SaveTeacherNoteRequest Input(AuthFixture test, string text = "A small discovery.", bool share = false, int? version = null)
        => new(Today(test), text, share, version);
    private static async Task<Student> Child(AuthFixture test, Guid? teacher = null, bool active = true)
    {
        await using var scope = test.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var student = new Student { FirstName = "Fictional", LastName = Guid.NewGuid().ToString("N"), TeacherUserId = teacher ?? test.Teacher.Id, IsActive = active };
        db.Students.Add(student); await db.SaveChangesAsync(); return student;
    }
    private static async Task SignIn(AuthFixture test)
    { Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Teacher)).StatusCode); await test.Csrf(); }
    private static async Task<TeacherEditor> Save(AuthFixture test, Guid child, SaveTeacherNoteRequest input)
    {
        var response = await test.Client.PutAsJsonAsync(Url(child), input);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<TeacherEditor>())!;
    }

    [PortalBootstrapFact]
    public async Task TeacherRoutesRequireRoleAndCsrfAndNeverExposeNotesToAdminOrParent()
    {
        await using var test = await AuthFixture.Create(); var child = await Child(test);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync("/portal/teacher/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync(Url(child.Id))).StatusCode);
        foreach (var user in new[] { test.Admin, test.Parent })
        {
            await test.Login(user); await test.Csrf();
            Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.GetAsync("/portal/teacher/students")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.GetAsync(Url(child.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.PutAsJsonAsync(Url(child.Id), Input(test))).StatusCode);
            await test.SignOut();
        }
        await SignIn(test); test.Client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PutAsJsonAsync(Url(child.Id), Input(test))).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task WorklistIncludesOnlyEnrolledAssignedStudentsAndSelectedDateStatus()
    {
        await using var test = await AuthFixture.Create(); var child = await Child(test);
        var other = await Child(test, test.Admin.Id); var left = await Child(test, active: false);
        await SignIn(test);
        var initial = (await test.Client.GetFromJsonAsync<TeacherWorklist>("/portal/teacher/students"))!;
        Assert.Equal(Today(test), initial.Today); Assert.Equal("not_started", Assert.Single(initial.Students).Status);
        await Save(test, child.Id, Input(test));
        var response = await test.Client.GetAsync("/portal/teacher/students"); Assert.True(response.Headers.CacheControl?.NoStore);
        var list = (await response.Content.ReadFromJsonAsync<TeacherWorklist>())!;
        Assert.Equal(child.Id, Assert.Single(list.Students).Id); Assert.Equal("draft", list.Students[0].Status);
        Assert.DoesNotContain("noteText", await response.Content.ReadAsStringAsync());
        var yesterday = (await test.Client.GetFromJsonAsync<TeacherWorklist>($"/portal/teacher/students?date={Today(test).AddDays(-1):yyyy-MM-dd}"))!;
        Assert.Equal("not_started", Assert.Single(yesterday.Students).Status);
        foreach (var id in new[] { other.Id, left.Id, Guid.NewGuid() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await test.Client.GetAsync(Url(id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await test.Client.PutAsJsonAsync(Url(id), Input(test))).StatusCode);
        }
    }

    [PortalBootstrapFact]
    public async Task DraftShareAndCorrectionPreserveAuthorshipAndFirstShareTime()
    {
        await using var test = await AuthFixture.Create(); var child = await Child(test); await SignIn(test);
        var draft = (await Save(test, child.Id, Input(test, "First paragraph.\n\nSecond paragraph.\n\nThird paragraph."))).Note!;
        Assert.Equal("draft", draft.Status); Assert.Equal(1, draft.Version); Assert.Null(draft.SharedAt);
        var shared = (await Save(test, child.Id, Input(test, draft.NoteText, true, draft.Version))).Note!;
        Assert.Equal("shared", shared.Status); Assert.Equal(2, shared.Version); Assert.Equal(shared.SharedAt, shared.UpdatedAt);
        var correction = (await Save(test, child.Id, Input(test, "A corrected discovery.", false, shared.Version))).Note!;
        Assert.Equal("shared", correction.Status); Assert.Equal(3, correction.Version); Assert.Equal(shared.SharedAt, correction.SharedAt);
        Assert.Equal(draft.AuthorName, correction.AuthorName); Assert.Equal(draft.CreatedAt, correction.CreatedAt);
        Assert.True(correction.UpdatedAt > correction.SharedAt);
        await using var scope = test.Factory.Services.CreateAsyncScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().DailyUpdates.ToArrayAsync());
        Assert.Empty(test.Factory.Mail.Messages);
    }

    [PortalBootstrapFact]
    public async Task StaleCreateAndStaleCorrectionAreRejectedWithoutOverwritingSavedText()
    {
        await using var test = await AuthFixture.Create(); var child = await Child(test); await SignIn(test);
        var first = (await Save(test, child.Id, Input(test, "First saved text."))).Note!;
        Assert.Equal(HttpStatusCode.Conflict, (await test.Client.PutAsJsonAsync(Url(child.Id), Input(test, "Stale first save."))).StatusCode);
        await Save(test, child.Id, Input(test, "Newer text.", true, first.Version));
        var conflict = await test.Client.PutAsJsonAsync(Url(child.Id), Input(test, "Stale correction.", true, first.Version));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode); Assert.Contains("Compare", await conflict.Content.ReadAsStringAsync());
        Assert.Equal("Newer text.", (await test.Client.GetFromJsonAsync<TeacherEditor>(Url(child.Id)))!.Note!.NoteText);
    }

    [PortalBootstrapFact]
    public async Task EmptyDraftIsAllowedButSharedNotesAndCorrectionsCannotBeBlankOrTooLong()
    {
        await using var test = await AuthFixture.Create(); var child = await Child(test); await SignIn(test);
        var draft = (await Save(test, child.Id, Input(test, ""))).Note!;
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PutAsJsonAsync(Url(child.Id), Input(test, " \n\t", true, draft.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PutAsJsonAsync(Url(child.Id), Input(test, new string('a', 2001), false, draft.Version))).StatusCode);
        var shared = (await Save(test, child.Id, Input(test, new string('a', 2000), true, draft.Version))).Note!;
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PutAsJsonAsync(Url(child.Id), Input(test, " ", false, shared.Version))).StatusCode);
        Assert.Equal(2000, (await test.Client.GetFromJsonAsync<TeacherEditor>(Url(child.Id)))!.Note!.NoteText.Length);
    }

    [PortalBootstrapFact]
    public async Task SchoolDateUsesJohannesburgAndRejectsFutureMalformedOrMissingValues()
    {
        await using var test = await AuthFixture.Create(); var child = await Child(test); await SignIn(test);
        foreach (var input in new[] { Input(test) with { UpdateDate = Today(test).AddDays(1) }, Input(test) with { UpdateDate = null }, Input(test) with { Share = null }, Input(test) with { ExpectedVersion = 0 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PutAsJsonAsync(Url(child.Id), input)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.GetAsync("/portal/teacher/students?date=not-a-date")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.GetAsync($"{Url(child.Id)}?date={Today(test).AddDays(1):yyyy-MM-dd}")).StatusCode);
        // Keep requests inside a live session while crossing the UTC/SA date boundary.
        var utc = test.Factory.Clock.GetUtcNow();
        test.Factory.Clock.Advance(new DateTimeOffset(utc.Year, utc.Month, utc.Day, 22, 5, 0, TimeSpan.Zero).AddDays(1) - utc);
        await SignIn(test);
        Assert.Equal(DateOnly.FromDateTime(test.Factory.Clock.GetUtcNow().UtcDateTime).AddDays(1),
            (await test.Client.GetFromJsonAsync<TeacherWorklist>("/portal/teacher/students"))!.Today);
    }

    [PortalBootstrapFact]
    public async Task ReassignedTeacherCanCorrectWithoutReplacingOriginalAuthorAndOldTeacherLosesAccess()
    {
        await using var test = await AuthFixture.Create(); var child = await Child(test); await SignIn(test);
        var original = (await Save(test, child.Id, Input(test, "Original teacher's note.", true))).Note!;
        await test.ChangeUser(test.Pending.Id, async (users, user) => {
            Assert.True((await users.AddPasswordAsync(user, "Fictional-New-Teacher-42!")).Succeeded);
            user.EmailConfirmed = true; user.FirstName = "Replacement"; Assert.True((await users.UpdateAsync(user)).Succeeded);
        });
        await using (var scope = test.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var record = (await db.Students.FindAsync(child.Id))!; record.TeacherUserId = test.Pending.Id; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await test.Client.GetAsync(Url(child.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await test.Client.PutAsJsonAsync(Url(child.Id), Input(test, "Old teacher edit.", true, original.Version))).StatusCode);
        await test.SignOut(); Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Pending, "Fictional-New-Teacher-42!")).StatusCode); await test.Csrf();
        var corrected = (await Save(test, child.Id, Input(test, "New teacher correction.", true, original.Version))).Note!;
        Assert.Equal(original.AuthorName, corrected.AuthorName); Assert.NotEqual(original.EditorName, corrected.EditorName);
        Assert.Equal(original.SharedAt, corrected.SharedAt);
    }

    [PortalBootstrapFact]
    public async Task ExpiryAndLeavingSchoolRejectLaterWritesWithoutChangingSavedNote()
    {
        await using var test = await AuthFixture.Create(); var child = await Child(test); await SignIn(test);
        var note = (await Save(test, child.Id, Input(test, "Keep this saved text."))).Note!;
        test.Factory.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.PutAsJsonAsync(Url(child.Id), Input(test, "Expired edit.", false, note.Version))).StatusCode);
        await SignIn(test);
        await using (var scope = test.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            var student = (await db.Students.FindAsync(child.Id))!; student.IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound, (await test.Client.PutAsJsonAsync(Url(child.Id), Input(test, "Departed child edit.", false, note.Version))).StatusCode);
        await using var check = test.Factory.Services.CreateAsyncScope();
        Assert.Equal("Keep this saved text.", (await check.ServiceProvider.GetRequiredService<PortalDbContext>().DailyUpdates.SingleAsync()).NoteText);
    }
}
