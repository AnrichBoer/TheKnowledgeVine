using KnowledgeVineAPI.Models;
using KnowledgeVineAPI.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeVineAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("contact-form")]
    public class EmailController : ControllerBase
    {
        private readonly IEmailService _emailService;

        public EmailController(IEmailService emailService)
        {
            _emailService = emailService;
        }

        [HttpPost("SendEmail")]
        public async Task<IActionResult> SendEmail(
            [FromBody] EmailRequest request,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(request.Website))
            {
                return Ok(new { message = "Thank you. Your enquiry has been received." });
            }

            await _emailService.SendEmailAsync(request, cancellationToken);

            return Ok(new { message = "Thank you. Your enquiry has been sent." });
        }
    }
}
