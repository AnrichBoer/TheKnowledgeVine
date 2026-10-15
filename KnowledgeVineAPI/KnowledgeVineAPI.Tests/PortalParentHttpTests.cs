using System.Net;
using System.Net.Http.Json;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static KnowledgeVineAPI.Tests.PortalAuthHttpTests;

namespace KnowledgeVineAPI.Tests;

public class PortalParentHttpTests
{
    private const string Password = "Fictional-Portal-Password-42!";
    private const string ChildrenUrl = "/portal/parent/children";
    private static string Feed(Guid child) => $"{ChildrenUrl}/{child}/updates";
    private static DateOnly Start(AuthFixture t) => new(t.Factory.Clock.GetUtcNow().AddHours(2).Year, 1, 1);
    private static async Task<Student> Child(AuthFixture t, bool linked = true, bool active = true, Guid? parent = null)
    {
        await using var scope = t.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var child = new Student { FirstName = "Fictional", LastName = Guid.NewGuid().ToString("N"), TeacherUserId = t.Teacher.Id, IsActive = active };
        db.Students.Add(child);
        if (linked) db.ParentStudents.Add(new() { StudentId = child.Id, ParentUserId = parent ?? t.Parent.Id, LinkedByUserId = t.Admin.Id });
        await db.SaveChangesAsync(); return child;
    }
    private static async Task<DailyUpdate> Note(AuthFixture t, Student child, DateOnly? date = null, bool shared = true)
    {
        await using var scope = t.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var note = new DailyUpdate { StudentId = child.Id, UpdateDate = date ?? Start(t), NoteText = "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.",
            Status = shared ? "shared" : "draft", CreatedByUserId = t.Teacher.Id, UpdatedByUserId = t.Teacher.Id };
        db.DailyUpdates.Add(note); await db.SaveChangesAsync(); return note;
    }
    private static async Task ChangeChild(AuthFixture t, Student child, bool unlink)
    {
        await using var scope = t.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        if (unlink) db.ParentStudents.Remove((await db.ParentStudents.FindAsync(t.Parent.Id, child.Id))!);
        else (await db.Students.FindAsync(child.Id))!.IsActive = false;
        await db.SaveChangesAsync();
    }

    [PortalBootstrapFact]
    public async Task ParentReadsRequireParentRoleAndAreReadOnlyAndNotCached()
    {
        await using var t = await AuthFixture.Create(); var child = await Child(t); var note = await Note(t, child);
        var paths = new[] { ChildrenUrl, Feed(child.Id), $"{Feed(child.Id)}/{note.Id}" };
        foreach (var path in paths) Assert.Equal(HttpStatusCode.Unauthorized, (await t.Client.GetAsync(path)).StatusCode);
        foreach (var user in new[] { t.Admin, t.Teacher })
        {
            await t.Login(user);
            foreach (var path in paths) Assert.Equal(HttpStatusCode.Forbidden, (await t.Client.GetAsync(path)).StatusCode);
            await t.SignOut();
        }
        await t.Login(t.Parent);
        foreach (var path in paths)
        {
            var response = await t.Client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(t.Parent.Email!, body); Assert.DoesNotContain(t.Teacher.Email!, body);
            Assert.DoesNotContain("passwordHash", body); Assert.DoesNotContain("createdByUserId", body);
        }
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await t.Client.PutAsJsonAsync($"{Feed(child.Id)}/{note.Id}", new { noteText = "Tamper" })).StatusCode);
        Assert.Empty(t.Factory.Mail.Messages);
    }

    [PortalBootstrapFact]
    public async Task FeedAndDirectReadsHideDraftsOtherFamiliesAndOlderOrFutureYears()
    {
        await using var t = await AuthFixture.Create(); var child = await Child(t); var sibling = await Child(t); var other = await Child(t, linked: false);
        var first = await Note(t, child); var newest = await Note(t, child, Start(t).AddYears(1).AddDays(-1));
        var old = await Note(t, child, Start(t).AddDays(-1)); var future = await Note(t, child, Start(t).AddYears(1));
        var draft = await Note(t, child, Start(t).AddDays(1), shared: false); var privateNote = await Note(t, other); var siblingNote = await Note(t, sibling);
        await t.Login(t.Parent);
        var children = (await t.Client.GetFromJsonAsync<ParentChildren>(ChildrenUrl))!;
        Assert.Equal(2, children.Children.Length); Assert.DoesNotContain(children.Children, c => c.Id == other.Id);
        var feed = (await t.Client.GetFromJsonAsync<ParentFeed>(Feed(child.Id)))!;
        Assert.Equal(new[] { newest.Id, first.Id }, feed.Notes.Select(n => n.Id)); Assert.Null(feed.NextBefore);
        foreach (var note in new[] { old, future, draft, privateNote, siblingNote })
            Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync($"{Feed(child.Id)}/{note.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync(Feed(other.Id))).StatusCode);
        Assert.Equal(siblingNote.Id, Assert.Single((await t.Client.GetFromJsonAsync<ParentFeed>(Feed(sibling.Id)))!.Notes).Id);
        await using var scope = t.Factory.Services.CreateAsyncScope();
        Assert.Equal(7, await scope.ServiceProvider.GetRequiredService<PortalDbContext>().DailyUpdates.CountAsync());
    }

    [PortalBootstrapFact]
    public async Task MultipleParentsCanReadTheSameChildAndEmptyStatesAreValid()
    {
        await using var t = await AuthFixture.Create(); var child = await Child(t); await t.Login(t.Parent);
        Assert.Empty((await t.Client.GetFromJsonAsync<ParentFeed>(Feed(child.Id)))!.Notes);
        await t.SignOut();
        await t.ChangeUser(t.Pending.Id, async (users, user) => {
            Assert.True((await users.AddPasswordAsync(user, Password)).Succeeded); user.EmailConfirmed = true;
            Assert.True((await users.UpdateAsync(user)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Parent")).Succeeded);
        });
        await t.Login(t.Pending);
        Assert.Empty((await t.Client.GetFromJsonAsync<ParentChildren>(ChildrenUrl))!.Children);
        await using (var scope = t.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.ParentStudents.Add(new() { ParentUserId = t.Pending.Id, StudentId = child.Id, LinkedByUserId = t.Admin.Id }); await db.SaveChangesAsync();
        }
        var note = await Note(t, child);
        Assert.Equal(note.Id, Assert.Single((await t.Client.GetFromJsonAsync<ParentFeed>(Feed(child.Id)))!.Notes).Id);
    }

    [PortalBootstrapTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DepartureOrUnlinkImmediatelyBlocksAllChildReadsButKeepsSiblingAndHistory(bool unlink)
    {
        await using var t = await AuthFixture.Create(); var child = await Child(t); var sibling = await Child(t);
        var note = await Note(t, child); var siblingNote = await Note(t, sibling); await t.Login(t.Parent);
        Assert.Equal(HttpStatusCode.OK, (await t.Client.GetAsync(Feed(child.Id))).StatusCode);
        await ChangeChild(t, child, unlink);
        Assert.Equal(sibling.Id, Assert.Single((await t.Client.GetFromJsonAsync<ParentChildren>(ChildrenUrl))!.Children).Id);
        foreach (var path in new[] { Feed(child.Id), $"{Feed(child.Id)}/{note.Id}" })
            Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync(path)).StatusCode);
        Assert.Equal(siblingNote.Id, Assert.Single((await t.Client.GetFromJsonAsync<ParentFeed>(Feed(sibling.Id)))!.Notes).Id);
        await using var scope = t.Factory.Services.CreateAsyncScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<PortalDbContext>().DailyUpdates.CountAsync());
    }

    [PortalBootstrapFact]
    public async Task PagingUsesUniqueSchoolDateAndRejectsInvalidInputs()
    {
        await using var t = await AuthFixture.Create(); var child = await Child(t);
        foreach (var day in new[] { 0, 1, 2 }) await Note(t, child, Start(t).AddDays(day));
        await t.Login(t.Parent);
        var first = (await t.Client.GetFromJsonAsync<ParentFeed>($"{Feed(child.Id)}?limit=2"))!;
        Assert.Equal(new[] { Start(t).AddDays(2), Start(t).AddDays(1) }, first.Notes.Select(n => n.UpdateDate));
        Assert.Equal(Start(t).AddDays(1), first.NextBefore);
        var next = (await t.Client.GetFromJsonAsync<ParentFeed>($"{Feed(child.Id)}?limit=2&before={first.NextBefore:yyyy-MM-dd}"))!;
        Assert.Equal(Start(t), Assert.Single(next.Notes).UpdateDate); Assert.Null(next.NextBefore);
        foreach (var query in new[] { "limit=0", "limit=51", "limit=no", "before=no", $"before={Start(t).AddDays(-1):yyyy-MM-dd}", $"before={Start(t).AddYears(1).AddDays(1):yyyy-MM-dd}" })
            Assert.Equal(HttpStatusCode.BadRequest, (await t.Client.GetAsync($"{Feed(child.Id)}?{query}")).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task CorrectionsShowOriginalTeacherAndLatestEditorWithoutDraftHistory()
    {
        await using var t = await AuthFixture.Create(); var child = await Child(t); var note = await Note(t, child);
        await t.ChangeUser(t.Pending.Id, async (users, user) => {
            Assert.True((await users.AddPasswordAsync(user, Password)).Succeeded); user.EmailConfirmed = true; user.FirstName = "Replacement";
            Assert.True((await users.UpdateAsync(user)).Succeeded);
        });
        await using (var scope = t.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>(); (await db.Students.FindAsync(child.Id))!.TeacherUserId = t.Pending.Id;
            await db.SaveChangesAsync(); var saved = (await db.DailyUpdates.FindAsync(note.Id))!;
            saved.UpdatedByUserId = t.Pending.Id; saved.NoteText = "Corrected discovery."; await db.SaveChangesAsync();
        }
        await t.Login(t.Parent);
        var result = (await t.Client.GetFromJsonAsync<ParentNote>($"{Feed(child.Id)}/{note.Id}"))!;
        Assert.Equal("Fictional Teacher", result.AuthorName); Assert.Equal("Replacement Teacher", result.EditorName);
        Assert.Equal(note.SharedAt, result.SharedAt); Assert.True(result.IsEdited); Assert.Equal("Corrected discovery.", result.NoteText);
    }

    [PortalBootstrapFact]
    public async Task SouthAfricanNewYearChangesBothFeedAndDirectReadsAtUtc22()
    {
        await using var t = await AuthFixture.Create(); var child = await Child(t); var start = Start(t).AddYears(1);
        var old = await Note(t, child, start.AddDays(-1)); var next = await Note(t, child, start);
        var boundary = new DateTimeOffset(start.Year - 1, 12, 31, 22, 0, 0, TimeSpan.Zero);
        t.Factory.Clock.Advance(boundary.AddMinutes(-1) - t.Factory.Clock.GetUtcNow()); await t.Login(t.Parent);
        var before = (await t.Client.GetFromJsonAsync<ParentFeed>(Feed(child.Id)))!;
        Assert.Equal(old.Id, Assert.Single(before.Notes).Id); Assert.Equal(boundary, before.NextYearAt);
        t.Factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var after = (await t.Client.GetFromJsonAsync<ParentFeed>(Feed(child.Id)))!;
        Assert.Equal(start.Year, after.Year); Assert.Equal(next.Id, Assert.Single(after.Notes).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync($"{Feed(child.Id)}/{old.Id}")).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task ExpiryDeactivationOrLostRoleCannotReadWithAnOldCookie()
    {
        await using var t = await AuthFixture.Create(); var child = await Child(t); var note = await Note(t, child); await t.Login(t.Parent);
        t.Factory.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await t.Client.GetAsync(Feed(child.Id))).StatusCode);
        await t.Login(t.Parent); await t.ChangeUser(t.Parent.Id, async (users, user) => { user.IsActive = false; Assert.True((await users.UpdateAsync(user)).Succeeded); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await t.Client.GetAsync($"{Feed(child.Id)}/{note.Id}")).StatusCode);
        await t.ChangeUser(t.Parent.Id, async (users, user) => { user.IsActive = true; Assert.True((await users.UpdateAsync(user)).Succeeded); });
        await t.Login(t.Parent); await t.ChangeUser(t.Parent.Id, async (users, user) => { Assert.True((await users.RemoveFromRoleAsync(user, "Parent")).Succeeded); });
        Assert.NotEqual(HttpStatusCode.OK, (await t.Client.GetAsync(ChildrenUrl)).StatusCode);
    }
}
