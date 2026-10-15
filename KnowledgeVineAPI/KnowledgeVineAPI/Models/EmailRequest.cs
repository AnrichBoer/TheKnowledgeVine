using System.ComponentModel.DataAnnotations;

namespace KnowledgeVineAPI.Models;

public class EmailRequest
{
    [Required, StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(30, MinimumLength = 7)]
    public string ContactNumber { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(4000)]
    public string Message { get; set; } = string.Empty;
}
