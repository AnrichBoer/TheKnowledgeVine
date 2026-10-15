using System.Security.Claims;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeVineAPI.Controllers;

[ApiController]
[Route("portal/parent")]
[Authorize(Policy = "PortalParent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortalParentController(PortalParentService parent) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("children")]
    public Task<ParentChildren> Children(CancellationToken ct) => parent.ChildrenAsync(Actor, ct);
    [HttpGet("children/{child:guid}/updates")]
    public Task<IActionResult> Feed(Guid child, DateOnly? before, CancellationToken ct, int limit = 20)
        => Handle(async () => Ok(await parent.FeedAsync(Actor, child, before, limit, ct)));
    [HttpGet("children/{child:guid}/updates/{note:guid}")]
    public Task<IActionResult> Note(Guid child, Guid note, CancellationToken ct)
        => Handle(async () => Ok(await parent.NoteAsync(Actor, child, note, ct)));
    private async Task<IActionResult> Handle(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (PortalParentException e) { return StatusCode(e.Status, new { message = e.Message }); }
    }
}
