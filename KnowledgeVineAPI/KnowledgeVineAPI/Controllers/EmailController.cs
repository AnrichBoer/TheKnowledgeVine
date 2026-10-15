using KnowledgeVineAPI.Models;
using KnowledgeVineAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeVineAPI.Controllers;

[ApiController]
[Route("[controller]")]
public class EmailController(IEmailService emailService, ILogger<EmailController> logger) : ControllerBase
{
    [HttpPost("SendEmail")]
    public async Task<IActionResult> SendEmail([FromBody] EmailRequest request)
    {
        try
        {
            if (!await emailService.SendEmailAsync(request))
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { success = false, message = "We could not send your enquiry. Please try again later." });

            return Ok(new { success = true });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Contact enquiry could not be sent");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { success = false, message = "We could not send your enquiry. Please try again later." });
        }
    }
}
