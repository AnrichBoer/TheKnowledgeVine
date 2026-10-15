using System.Security.Claims;
using KnowledgeVineAPI.Services.Portal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeVineAPI.Controllers;

[ApiController]
[Route("portal/teacher")]
[Authorize(Policy = "PortalTeacher")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortalTeacherController(PortalTeacherService teacher) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("students")]
    public Task<IActionResult> Students(DateOnly? date, CancellationToken ct)
        => Handle(async () => Ok(await teacher.WorklistAsync(Actor, date, ct)));
    [HttpGet("students/{id:guid}/note")]
    public Task<IActionResult> Note(Guid id, DateOnly? date, CancellationToken ct)
        => Handle(async () => Ok(await teacher.EditorAsync(Actor, id, date, ct)));
    [HttpPut("students/{id:guid}/note")]
    public Task<IActionResult> Save(Guid id, SaveTeacherNoteRequest input, CancellationToken ct)
        => Handle(async () => Ok(await teacher.SaveAsync(Actor, id, input, ct)));
    private async Task<IActionResult> Handle(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (PortalTeacherException e) { return StatusCode(e.Status, new { message = e.Message }); }
    }
}
