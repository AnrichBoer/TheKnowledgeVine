using System.Net;
using System.Net.Http.Json;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static KnowledgeVineAPI.Tests.PortalAuthHttpTests;

namespace KnowledgeVineAPI.Tests;

// Whole-workflow checks use real HTTP, Identity, PostgreSQL and the released note triggers.
// All school/account rows are session-local temporary tables; delivery is captured in memory.
public class PortalWorkflowHttpTests
{
    private const string SetupPassword = "Fictional-Changed-Password-73!";
    private const string Paragraphs = "Today we explored the colour tablets and matched several shades independently."
        + "\n\nWe enjoyed working together to build a tower, taking turns and helping our friend."
        + "\n\nOur afternoon finished with a favourite story and time in the garden.";

    private static string NoteUrl(Guid id) => $"/portal/teacher/students/{id}/note";
    private static string FeedUrl(Guid id) => $"/portal/parent/children/{id}/updates";
    private static async Task<AdminDirectory> Directory(AuthFixture t)
        => (await t.Client.GetFromJsonAsync<AdminDirectory>("/portal/admin/directory"))!;
    private static async Task<HttpResponseMessage> Login(AuthFixture t, PortalUser user, bool invited = false)
    {
        var response = invited ? await t.Login(user, SetupPassword) : await t.Login(user);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await t.Csrf();
        return response;
    }
    private static async Task<PortalUser> User(AuthFixture t, Guid id)
    {
        await using var scope = t.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PortalDbContext>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }
    private static async Task<AdminStudent> Child(AuthFixture t, Guid? teacher = null, params Guid[] parents)
    {
        var response = await t.Client.PostAsJsonAsync("/portal/admin/students",
            new SaveStudentRequest("Demo", "Learner", teacher, true, parents, null, []));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminStudent>())!;
    }
    private static async Task<AccountInvitationResult> Invite(AuthFixture t, string role, params Guid[] children)
    {
        var response = await t.Client.PostAsJsonAsync("/portal/admin/accounts", new CreateAccountRequest(
            "Demo", role, Guid.NewGuid().ToString("N") + "@example.test", role, children));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AccountInvitationResult>())!;
    }
    private static async Task Complete(AuthFixture t, PortalAccountMessage message)
    {
        Assert.Equal(HttpStatusCode.OK, (await t.Complete(message, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await t.Client.GetAsync("/portal/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await t.Complete(message, true)).StatusCode);
    }
    private static async Task<TeacherNote> Save(AuthFixture t, Guid child, string text, bool share, int? version = null)
    {
        var today = DateOnly.FromDateTime(t.Factory.Clock.GetUtcNow().AddHours(2).DateTime);
        var response = await t.Client.PutAsJsonAsync(NoteUrl(child), new SaveTeacherNoteRequest(today, text, share, version));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<TeacherEditor>())!.Note!;
    }
    private static SaveStudentRequest Edit(AdminStudent child, bool active)
        => new(child.FirstName, child.LastName, child.TeacherUserId, active, child.ParentIds, child.UpdatedAt, child.ParentIds);

    [PortalBootstrapFact]
    public async Task InvitationsThroughDraftShareCorrectionAndDepartureWorkAcrossAnApiRestart()
    {
        await using var t = await AuthFixture.Create();
        await Login(t, t.Admin);
        var child = await Child(t);
        var teacherInvite = await Invite(t, "Teacher");
        var teacherMessage = t.Factory.Mail.Messages.Last();
        var parentInvite = await Invite(t, "Parent", child.Id);
        var parentMessage = t.Factory.Mail.Messages.Last();
        var teacher = await User(t, teacherInvite.Account.Id);
        var parent = await User(t, parentInvite.Account.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await t.Login(teacher, SetupPassword)).StatusCode);
        await t.Csrf();
        Assert.Equal(HttpStatusCode.BadRequest, (await t.Client.PutAsJsonAsync($"/portal/admin/students/{child.Id}",
            Edit(child, true) with { TeacherUserId = teacher.Id })).StatusCode);

        await Complete(t, teacherMessage);
        // Assignment becomes available immediately after setup, before the teacher's first sign-in.
        await Login(t, t.Admin);
        child = (await Directory(t)).Students.Single(s => s.Id == child.Id);
        Assert.True((await Directory(t)).Accounts.Single(a => a.Id == teacher.Id).IsReady);
        Assert.Equal(HttpStatusCode.OK, (await t.Client.PutAsJsonAsync($"/portal/admin/students/{child.Id}",
            Edit(child, true) with { TeacherUserId = teacher.Id })).StatusCode);
        await Complete(t, parentMessage);

        await Login(t, teacher, invited: true);
        Assert.Equal(child.Id, Assert.Single((await t.Client.GetFromJsonAsync<TeacherWorklist>("/portal/teacher/students"))!.Students).Id);
        var draft = await Save(t, child.Id, Paragraphs, false);
        Assert.Equal("draft", draft.Status);
        await Login(t, parent, invited: true);
        Assert.Equal(child.Id, Assert.Single((await t.Client.GetFromJsonAsync<ParentChildren>("/portal/parent/children"))!.Children).Id);
        Assert.Empty((await t.Client.GetFromJsonAsync<ParentFeed>(FeedUrl(child.Id)))!.Notes);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync($"{FeedUrl(child.Id)}/{draft.Id}")).StatusCode);

        await Login(t, teacher, invited: true);
        var shared = await Save(t, child.Id, Paragraphs, true, draft.Version);
        var parentLogin = await Login(t, parent, invited: true);
        var cookie = parentLogin.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("KnowledgeVine.Portal=")).Split(';')[0];
        Assert.Equal(Paragraphs, Assert.Single((await t.Client.GetFromJsonAsync<ParentFeed>(FeedUrl(child.Id)))!.Notes).NoteText);

        await t.RestartApi();
        using (var resumed = t.Factory.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false }))
        {
            resumed.DefaultRequestHeaders.Add("Cookie", cookie);
            Assert.Equal(HttpStatusCode.OK, (await resumed.GetAsync("/portal/auth/session")).StatusCode);
            var response = await resumed.GetAsync($"{FeedUrl(child.Id)}/{shared.Id}");
            Assert.True(response.Headers.CacheControl?.NoStore);
            Assert.Equal(Paragraphs, (await response.Content.ReadFromJsonAsync<ParentNote>())!.NoteText);
        }
        await Login(t, teacher, invited: true);
        var persisted = (await t.Client.GetFromJsonAsync<TeacherEditor>(NoteUrl(child.Id)))!.Note!;
        Assert.Equal(shared.Id, persisted.Id); Assert.Equal(shared.Version, persisted.Version);
        var corrected = await Save(t, child.Id, Paragraphs + " A corrected detail.", false, persisted.Version);
        await Login(t, parent, invited: true);
        var visible = Assert.Single((await t.Client.GetFromJsonAsync<ParentFeed>(FeedUrl(child.Id)))!.Notes);
        Assert.Equal(corrected.NoteText, visible.NoteText); Assert.True(visible.IsEdited);
        Assert.Equal(shared.SharedAt, visible.SharedAt); Assert.Equal("Demo Teacher", visible.AuthorName);
        Assert.Equal(HttpStatusCode.Forbidden, (await t.Client.GetAsync(NoteUrl(child.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await t.Client.GetAsync("/portal/admin/directory")).StatusCode);

        await Login(t, t.Admin);
        child = (await Directory(t)).Students.Single(s => s.Id == child.Id);
        Assert.Equal(HttpStatusCode.OK, (await t.Client.PutAsJsonAsync($"/portal/admin/students/{child.Id}", Edit(child, false))).StatusCode);
        await Login(t, parent, invited: true);
        Assert.Empty((await t.Client.GetFromJsonAsync<ParentChildren>("/portal/parent/children"))!.Children);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync($"{FeedUrl(child.Id)}/{shared.Id}")).StatusCode);
        await Login(t, teacher, invited: true);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync(NoteUrl(child.Id))).StatusCode);
        await using var scope = t.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal(corrected.NoteText, (await db.DailyUpdates.SingleAsync()).NoteText);
        Assert.Single(await db.ParentStudents.ToArrayAsync());
    }

    [PortalBootstrapFact]
    public async Task DeliveryRetryAndPendingSetupSurviveRestartWithoutDuplicateAccountsOrLinks()
    {
        await using var t = await AuthFixture.Create(); await Login(t, t.Admin);
        var child = await Child(t);
        t.Factory.Mail.Fail = true;
        var failed = await Invite(t, "Parent", child.Id);
        Assert.Equal("failed", failed.Delivery); Assert.Empty(t.Factory.Mail.Messages);
        Assert.False(failed.Account.IsReady);
        await t.RestartApi(); await Login(t, t.Admin);
        var pending = (await Directory(t)).Accounts.Single(a => a.Id == failed.Account.Id);
        Assert.Equal(new[] { child.Id }, pending.StudentIds);
        var response = await t.Client.PostAsJsonAsync($"/portal/admin/accounts/{pending.Id}/resend", new { version = pending.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var message = Assert.Single(t.Factory.Mail.Messages);
        Assert.True(message.IsResend); Assert.True(message.InitialSetup);
        Assert.Equal(pending.Email, message.Recipient);
        await t.RestartApi();
        await Complete(t, message);
        var parent = await User(t, pending.Id);
        await Login(t, parent, invited: true);
        Assert.Equal(child.Id, Assert.Single((await t.Client.GetFromJsonAsync<ParentChildren>("/portal/parent/children"))!.Children).Id);
        await t.SignOut();
        Assert.Equal(HttpStatusCode.Unauthorized, (await t.Client.GetAsync(FeedUrl(child.Id))).StatusCode);
        await using var scope = t.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == pending.Email));
        Assert.Single(await db.ParentStudents.ToArrayAsync());
        Assert.Empty(await db.DailyUpdates.ToArrayAsync());
    }

    [PortalBootstrapFact]
    public async Task IndependentlyInvitedFamiliesAndTeachersCannotCrossReadAndIdleExpiryStillApplies()
    {
        await using var t = await AuthFixture.Create(); await Login(t, t.Admin);
        var first = await Child(t, t.Teacher.Id, t.Parent.Id);
        var teacherInvite = await Invite(t, "Teacher"); var teacherMessage = t.Factory.Mail.Messages.Last();
        await Complete(t, teacherMessage); await Login(t, t.Admin);
        var teacher = await User(t, teacherInvite.Account.Id);
        var second = await Child(t, teacher.Id);
        var parentInvite = await Invite(t, "Parent", second.Id); var parentMessage = t.Factory.Mail.Messages.Last();
        await Complete(t, parentMessage);
        var parent = await User(t, parentInvite.Account.Id);
        await Login(t, t.Teacher);
        var firstNote = await Save(t, first.Id, "First family's discovery.", true);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync(NoteUrl(second.Id))).StatusCode);
        await Login(t, teacher, invited: true);
        var secondNote = await Save(t, second.Id, "Second family's discovery.", true);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync(NoteUrl(first.Id))).StatusCode);
        foreach (var (account, invited, own, other, otherNote) in new[] {
            (t.Parent, false, first, second, secondNote), (parent, true, second, first, firstNote) })
        {
            await Login(t, account, invited);
            Assert.Equal(own.Id, Assert.Single((await t.Client.GetFromJsonAsync<ParentChildren>("/portal/parent/children"))!.Children).Id);
            Assert.Single((await t.Client.GetFromJsonAsync<ParentFeed>(FeedUrl(own.Id)))!.Notes);
            Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync(FeedUrl(other.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await t.Client.GetAsync($"{FeedUrl(other.Id)}/{otherNote.Id}")).StatusCode);
        }
        t.Factory.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await t.Client.GetAsync(FeedUrl(second.Id))).StatusCode);
        t.Factory.Clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await t.Client.GetAsync(FeedUrl(second.Id))).StatusCode);
    }
}
