using KnowledgeVineAPI.Models;
using KnowledgeVineAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeVineAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EmailController : ControllerBase
    {
        private IEmailService _emailService;

        public EmailController(IEmailService emailService)
        {
            _emailService = emailService;
        }

        [HttpPost("SendEmail")]
        public async Task<IActionResult> SendEmail([FromBody] EmailRequest request)
        {
            try
            {
                var result = await _emailService.SendEmailAsync(request);
                return Ok(result);
            }
            catch (Exception e)
            {
                return BadRequest(e.Message);
            }
        }
    }
}
