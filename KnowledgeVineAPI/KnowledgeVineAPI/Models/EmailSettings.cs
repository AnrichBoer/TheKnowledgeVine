using System.ComponentModel.DataAnnotations;

namespace KnowledgeVineAPI.Models
{
    public sealed class EmailSettings
    {
        public const string SectionName = "EmailSettings";

        [Required]
        public string SmtpHost { get; init; } = string.Empty;

        [Range(1, 65535)]
        public int SmtpPort { get; init; } = 587;

        [Required]
        [EmailAddress]
        public string FromEmail { get; init; } = string.Empty;

        [Required]
        public string Password { get; init; } = string.Empty;
    }
}
