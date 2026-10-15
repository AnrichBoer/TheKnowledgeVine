using System.Net;
using System.Net.Http.Json;
using KnowledgeVineAPI.Data;
using KnowledgeVineAPI.Models.Portal;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static KnowledgeVineAPI.Tests.PortalAuthHttpTests;

namespace KnowledgeVineAPI.Tests;

public class PortalAdminHttpTests
{
    private static CreateAccountRequest Account(string role, params Guid[] children) => new("Fictional", "Family",
        Guid.NewGuid().ToString("N") + "@example.test", role, children);
    private static SaveStudentRequest Child(Guid? teacher = null, params Guid[] parents) => new("Fictional", "Child", teacher, true, parents, null, []);
    private static async Task SignIn(AuthFixture test)
    { Assert.Equal(HttpStatusCode.OK, (await test.Login(test.Admin)).StatusCode); await test.Csrf(); }
    private static async Task<AdminDirectory> Data(AuthFixture test) => (await test.Client.GetFromJsonAsync<AdminDirectory>("/portal/admin/directory"))!;
    private static async Task<AdminStudent> AddChild(AuthFixture test, SaveStudentRequest? input = null)
    {
        var response = await test.Client.PostAsJsonAsync("/portal/admin/students", input ?? Child());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AdminStudent>())!;
    }
    private static SaveStudentRequest Edit(AdminStudent s, Guid? teacher = null, Guid[]? parents = null, bool? active = null)
        => new(s.FirstName, s.LastName, teacher ?? s.TeacherUserId, active ?? s.IsActive, parents ?? s.ParentIds, s.UpdatedAt, s.ParentIds);

    [PortalBootstrapFact]
    public async Task AdminDirectoryAndMutationsEnforceRoleAndCsrf()
    {
        await using var test = await AuthFixture.Create();
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync("/portal/admin/directory")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await test.Client.GetAsync($"/portal/admin/accounts/{test.Pending.Id}/invitation-preview")).StatusCode);
        foreach (var user in new[] { test.Teacher, test.Parent })
        {
            await test.Login(user); await test.Csrf();
            Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.GetAsync("/portal/admin/directory")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.PostAsJsonAsync("/portal/admin/accounts", Account("Parent"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.PostAsJsonAsync("/portal/admin/students", Child())).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await test.Client.GetAsync($"/portal/admin/accounts/{test.Pending.Id}/invitation-preview")).StatusCode);
            await test.SignOut();
        }
        await SignIn(test);
        test.Client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/admin/accounts", Account("Teacher"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/admin/students", Child())).StatusCode);
        await test.Csrf();
        var directory = await test.Client.GetAsync("/portal/admin/directory");
        Assert.True(directory.Headers.CacheControl?.NoStore);
        var json = await directory.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
    }

    [PortalBootstrapFact]
    public async Task ParentCreationLinksSiblingsAtomicallyWithoutSettingAPassword()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var first = await AddChild(test); var second = await AddChild(test);
        var input = Account("Parent", first.Id, second.Id, first.Id);
        var response = await test.Client.PostAsJsonAsync("/portal/admin/accounts", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<AccountInvitationResult>())!;
        Assert.False(result.Account.IsReady); Assert.True(result.Account.IsActive);
        Assert.Equal(new[] { "Parent" }, result.Account.Roles);
        Assert.Equal(2, result.Account.StudentIds.Length);
        Assert.Equal(input.Email, Assert.Single(test.Factory.Mail.Messages).Recipient);
        await using var scope = test.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        var user = await db.Users.FindAsync(result.Account.Id);
        Assert.Null(user!.PasswordHash); Assert.False(user.EmailConfirmed);
        Assert.All(await db.ParentStudents.ToArrayAsync(), link => Assert.Equal(test.Admin.Id, link.LinkedByUserId));
    }

    [PortalBootstrapFact]
    public async Task InvalidChildrenRollBackAccountCreationAndAdminRolesCannotBeGranted()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var count = (await Data(test)).Accounts.Length;
        Assert.Equal(HttpStatusCode.NotFound, (await test.Client.PostAsJsonAsync("/portal/admin/accounts", Account("Parent", Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/admin/accounts", Account("Admin"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/admin/accounts", Account("Teacher", Guid.NewGuid()))).StatusCode);
        Assert.Equal(count, (await Data(test)).Accounts.Length); Assert.Empty(test.Factory.Mail.Messages);
    }

    [PortalBootstrapFact]
    public async Task NormalizedDuplicateEmailReturnsExistingAccountWithoutGrantingAnotherRole()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var response = await test.Client.PostAsJsonAsync("/portal/admin/accounts", Account("Parent") with { Email = test.Teacher.Email!.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync(); Assert.Contains(test.Teacher.Id.ToString(), content);
        Assert.Equal(new[] { "Teacher" }, (await Data(test)).Accounts.Single(a => a.Id == test.Teacher.Id).Roles);
        Assert.Empty(test.Factory.Mail.Messages);
    }

    [PortalBootstrapFact]
    public async Task FailedInvitationPreservesAccountAndChildrenAndCanBeRetried()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var child = await AddChild(test); test.Factory.Mail.Fail = true;
        var response = await test.Client.PostAsJsonAsync("/portal/admin/accounts", Account("Parent", child.Id));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<AccountInvitationResult>())!;
        Assert.Equal("failed", result.Delivery); Assert.Contains(child.Id, result.Account.StudentIds);
        Assert.False(result.Account.IsReady);
        test.Factory.Mail.Fail = false;
        response = await test.Client.PostAsJsonAsync($"/portal/admin/accounts/{result.Account.Id}/resend", new { version = result.Account.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual("failed", (await response.Content.ReadFromJsonAsync<AccountInvitationResult>())!.Delivery);
        Assert.Single(test.Factory.Mail.Messages);
    }

    [PortalBootstrapFact]
    public async Task TeachersCannotBeAssignedUntilSetupIsComplete()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/admin/students", Child(test.Pending.Id))).StatusCode);
        var response = await test.Client.PostAsJsonAsync($"/portal/admin/accounts/{test.Pending.Id}/resend", new { version = test.Pending.ConcurrencyStamp });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Complete(Assert.Single(test.Factory.Mail.Messages), true)).StatusCode);
        // Password completion signs out this request's cookie. Restore the admin to manage assignments.
        await SignIn(test);
        var child = await AddChild(test, Child(test.Pending.Id)); Assert.Equal(test.Pending.Id, child.TeacherUserId);
        Assert.True((await Data(test)).Accounts.Single(a => a.Id == test.Pending.Id).IsReady);
    }

    [PortalBootstrapFact]
    public async Task ResendingReplacesPendingLinkAndRejectsStaleReadyAndInactiveAccounts()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var a = (await Data(test)).Accounts.Single(a => a.Id == test.Pending.Id);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.PostAsJsonAsync($"/portal/admin/accounts/{a.Id}/resend", new { version = a.Version })).StatusCode);
        var first = test.Factory.Mail.Messages.Last();
        Assert.Equal(HttpStatusCode.Conflict, (await test.Client.PostAsJsonAsync($"/portal/admin/accounts/{a.Id}/resend", new { version = a.Version })).StatusCode);
        a = (await Data(test)).Accounts.Single(a => a.Id == test.Pending.Id);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.PostAsJsonAsync($"/portal/admin/accounts/{a.Id}/resend", new { version = a.Version })).StatusCode);
        var second = test.Factory.Mail.Messages.Last();
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Complete(first, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await test.Complete(second, true)).StatusCode);
        await SignIn(test);
        a = (await Data(test)).Accounts.Single(a => a.Id == test.Pending.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync($"/portal/admin/accounts/{a.Id}/resend", new { version = a.Version })).StatusCode);
        await test.Client.PutAsJsonAsync($"/portal/admin/accounts/{a.Id}/state", new { version = a.Version, isActive = false });
        a = (await Data(test)).Accounts.Single(a => a.Id == test.Pending.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync($"/portal/admin/accounts/{a.Id}/resend", new { version = a.Version })).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task TeacherDeactivationClearsAssignmentsAndInvalidatesItsCookie()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var child = await AddChild(test, Child(test.Teacher.Id, test.Parent.Id));
        await test.SignOut();
        var login = await test.Login(test.Teacher);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("KnowledgeVine.Portal=")).Split(';')[0];
        await test.SignOut(); await SignIn(test);
        var a = (await Data(test)).Accounts.Single(a => a.Id == test.Teacher.Id);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.PutAsJsonAsync($"/portal/admin/accounts/{a.Id}/state", new { version = a.Version, isActive = false })).StatusCode);
        var data = await Data(test);
        Assert.Null(data.Students.Single(s => s.Id == child.Id).TeacherUserId);
        Assert.Contains(test.Parent.Id, data.Students.Single(s => s.Id == child.Id).ParentIds);
        using var old = test.Factory.CreateClient(new() { HandleCookies = false }); old.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/portal/auth/session")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PutAsJsonAsync($"/portal/admin/students/{child.Id}", Edit(data.Students.Single(s => s.Id == child.Id), test.Teacher.Id))).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task ParentLinksAllowSiblingsAndMultipleParentsAndDetectConcurrentChanges()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var child = await AddChild(test, Child(null, test.Parent.Id)); var sibling = await AddChild(test);
        var pending = (await test.Client.PostAsJsonAsync("/portal/admin/accounts", Account("Parent", child.Id))).Content;
        var secondParent = (await pending.ReadFromJsonAsync<AccountInvitationResult>())!.Account;
        Assert.Equal(2, (await Data(test)).Students.Single(s => s.Id == child.Id).ParentIds.Length);
        var parent = (await Data(test)).Accounts.Single(a => a.Id == test.Parent.Id);
        var input = new ParentLinksRequest(parent.Version, [sibling.Id], parent.StudentIds);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.PutAsJsonAsync($"/portal/admin/accounts/{parent.Id}/children", input)).StatusCode);
        var data = await Data(test);
        Assert.Equal(new[] { secondParent.Id }, data.Students.Single(s => s.Id == child.Id).ParentIds);
        Assert.Equal(new[] { test.Parent.Id }, data.Students.Single(s => s.Id == sibling.Id).ParentIds);
        Assert.Equal(HttpStatusCode.Conflict, (await test.Client.PutAsJsonAsync($"/portal/admin/accounts/{parent.Id}/children", input)).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task StaleStudentEditsAndLinkEditsCannotSilentlyOverwriteChanges()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var child = await AddChild(test);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.PutAsJsonAsync($"/portal/admin/students/{child.Id}", Edit(child) with { FirstName = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await test.Client.PutAsJsonAsync($"/portal/admin/students/{child.Id}", Edit(child))).StatusCode);
        child = (await Data(test)).Students.Single(s => s.Id == child.Id);
        var parent = (await Data(test)).Accounts.Single(a => a.Id == test.Parent.Id);
        await test.Client.PutAsJsonAsync($"/portal/admin/accounts/{parent.Id}/children", new ParentLinksRequest(parent.Version, [child.Id], []));
        Assert.Equal(HttpStatusCode.Conflict, (await test.Client.PutAsJsonAsync($"/portal/admin/students/{child.Id}", Edit(child))).StatusCode);
        Assert.Contains(parent.Id, (await Data(test)).Students.Single(s => s.Id == child.Id).ParentIds);
    }

    [PortalBootstrapFact]
    public async Task LeavingSchoolRetainsChildRelationshipsAndNotesAndCanBeReversed()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var child = await AddChild(test, Child(test.Teacher.Id, test.Parent.Id));
        await using (var scope = test.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.DailyUpdates.Add(new() { StudentId = child.Id, UpdateDate = DateOnly.FromDateTime(DateTime.UtcNow), NoteText = "Fictional retained note", CreatedByUserId = test.Teacher.Id, UpdatedByUserId = test.Teacher.Id });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await test.Client.PutAsJsonAsync($"/portal/admin/students/{child.Id}", Edit(child, active: false))).StatusCode);
        child = (await Data(test)).Students.Single(s => s.Id == child.Id);
        Assert.False(child.IsActive); Assert.Contains(test.Parent.Id, child.ParentIds);
        await using (var scope = test.Factory.Services.CreateAsyncScope())
            Assert.Single(await scope.ServiceProvider.GetRequiredService<PortalDbContext>().DailyUpdates.ToArrayAsync());
        Assert.Equal(HttpStatusCode.OK, (await test.Client.PutAsJsonAsync($"/portal/admin/students/{child.Id}", Edit(child, active: true))).StatusCode);
        Assert.True((await Data(test)).Students.Single(s => s.Id == child.Id).IsActive);
    }

    [PortalBootstrapFact]
    public async Task AdminAccountCannotBeDeactivatedAndMissingNamesAreRejected()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var a = (await Data(test)).Accounts.Single(a => a.Id == test.Admin.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PutAsJsonAsync($"/portal/admin/accounts/{a.Id}/state", new { version = a.Version, isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/admin/accounts", Account("Parent") with { FirstName = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await test.Client.PostAsJsonAsync("/portal/admin/students", Child() with { LastName = "   " })).StatusCode);
        Assert.True((await Data(test)).Accounts.Single(a => a.Id == test.Admin.Id).IsActive);
    }

    [PortalBootstrapFact]
    public async Task PrivateInvitationPreviewRequiresAdminAndAValidUnconsumedLink()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        await test.Client.PostAsJsonAsync($"/portal/admin/accounts/{test.Pending.Id}/resend", new { version = test.Pending.ConcurrencyStamp });
        var message = Assert.Single(test.Factory.Mail.Messages);
        await using (var scope = test.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PortalAccountPreviewMail>().SendAsync(message, CancellationToken.None);
        var response = await test.Client.GetAsync($"/portal/admin/accounts/{test.Pending.Id}/invitation-preview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl?.NoStore);
        var preview = (await response.Content.ReadFromJsonAsync<PortalAccountPreviewMail.InvitationPreview>())!;
        Assert.Equal(message.Link.AbsoluteUri, preview.SetupUrl);
        await test.Complete(message, true); await SignIn(test);
        Assert.Equal(HttpStatusCode.NotFound, (await test.Client.GetAsync($"/portal/admin/accounts/{test.Pending.Id}/invitation-preview")).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task ExpiredTestInvitationIsNotPresentedAsUsable()
    {
        await using var test = await AuthFixture.Create(expireSetup: true); await SignIn(test);
        await test.Client.PostAsJsonAsync($"/portal/admin/accounts/{test.Pending.Id}/resend", new { version = test.Pending.ConcurrencyStamp });
        await using (var scope = test.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<PortalAccountPreviewMail>().SendAsync(Assert.Single(test.Factory.Mail.Messages), CancellationToken.None);
        Assert.Equal(HttpStatusCode.NotFound, (await test.Client.GetAsync($"/portal/admin/accounts/{test.Pending.Id}/invitation-preview")).StatusCode);
    }

    [PortalBootstrapFact]
    public async Task ATeacherWithAParentRoleDoesNotAutomaticallyBecomeLinkedToAssignedChildren()
    {
        await using var test = await AuthFixture.Create(); await SignIn(test);
        var child = await AddChild(test, Child(test.Teacher.Id));
        await test.ChangeUser(test.Teacher.Id, async (users, user) => Assert.True((await users.AddToRoleAsync(user, "Parent")).Succeeded));
        var account = (await Data(test)).Accounts.Single(a => a.Id == test.Teacher.Id);
        Assert.Contains("Parent", account.Roles); Assert.Empty(account.StudentIds);
        Assert.Empty((await Data(test)).Students.Single(s => s.Id == child.Id).ParentIds);
        Assert.Equal(HttpStatusCode.OK, (await test.Client.PutAsJsonAsync($"/portal/admin/accounts/{account.Id}/children",
            new ParentLinksRequest(account.Version, [child.Id], []))).StatusCode);
        Assert.Equal(new[] { account.Id }, (await Data(test)).Students.Single(s => s.Id == child.Id).ParentIds);
    }
}
