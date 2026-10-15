using System.Security.Claims;
using System.Text;
using KnowledgeVineAPI.Models.Portal;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeVineAPI.Controllers;

[ApiController]
[Route("portal/admin")]
[Authorize(Policy = "PortalAdmin")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortalAdminController(PortalAdminService admin, PortalAccountPreviewMail previews,
    UserManager<PortalUser> users, PortalMailTemplates templates) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("directory")]
    public async Task<IActionResult> Directory(CancellationToken ct) => Ok(await admin.DirectoryAsync(ct));
    [HttpPost("accounts")]
    public Task<IActionResult> CreateAccount(CreateAccountRequest request, CancellationToken ct)
        => Handle(async () => StatusCode(201, await admin.CreateAccountAsync(Actor, request, ct)));
    [HttpPost("accounts/{id:guid}/resend")]
    public Task<IActionResult> Resend(Guid id, ResendInvitationRequest request, CancellationToken ct)
        => Handle(async () => Ok(await admin.ResendAsync(id, request.Version, ct)));
    [HttpPut("accounts/{id:guid}/state")]
    public Task<IActionResult> AccountState(Guid id, AccountStateRequest request, CancellationToken ct)
        => Handle(async () => Ok(await admin.SetAccountStateAsync(id, request, ct)));
    [HttpPut("accounts/{id:guid}/children")]
    public Task<IActionResult> ParentLinks(Guid id, ParentLinksRequest request, CancellationToken ct)
        => Handle(async () => Ok(await admin.SetParentLinksAsync(Actor, id, request, ct)));
    [HttpPost("students")]
    public Task<IActionResult> CreateStudent(SaveStudentRequest request, CancellationToken ct)
        => Handle(async () => StatusCode(201, await admin.SaveStudentAsync(Actor, null, request, ct)));
    [HttpPut("students/{id:guid}")]
    public Task<IActionResult> SaveStudent(Guid id, SaveStudentRequest request, CancellationToken ct)
        => Handle(async () => Ok(await admin.SaveStudentAsync(Actor, id, request, ct)));
    [HttpGet("accounts/{id:guid}/invitation-preview")]
    public Task<IActionResult> Preview(Guid id, CancellationToken ct) => Handle(async () =>
    {
        if (!previews.InvitationPreviewEnabled) return NotFound();
        var account = (await admin.DirectoryAsync(ct)).Accounts.SingleOrDefault(a => a.Id == id);
        if (account is null || !account.IsActive || account.IsReady) return NotFound();
        var preview = await previews.LatestAsync(account.Email, ct);
        if (preview is null || !Uri.TryCreate(preview.SetupUrl, UriKind.Absolute, out var url)
            || url.GetLeftPart(UriPartial.Authority) != templates.PublicOrigin().GetLeftPart(UriPartial.Authority)
            || url.AbsolutePath != "/portal/setup-password") return NoPreview();
        var values = QueryHelpers.ParseQuery(url.Fragment.TrimStart('#'));
        if (!Guid.TryParse(values["userId"], out var linkedId) || linkedId != id) return NoPreview();
        var user = await users.FindByIdAsync(id.ToString());
        try
        {
            var token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(values["token"].ToString()));
            return user is not null && await users.VerifyUserTokenAsync(user, PortalAccountLinks.SetupProvider,
                PortalAccountLinks.SetupPurpose, token) ? Ok(preview) : NoPreview();
        }
        catch (FormatException) { return NoPreview(); }
    });
    private IActionResult NoPreview() => NotFound(new { message = "No valid test invitation is available. Resend the invitation and try again." });
    private async Task<IActionResult> Handle(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (PortalAdminException error)
        { return StatusCode(error.Status, new { message = error.Message, existingAccountId = error.ExistingAccountId }); }
    }
}
