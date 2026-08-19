using System.ComponentModel.DataAnnotations;

namespace KnowledgeVineAPI.Models
{
    public class EmailRequest
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Phone]
        [StringLength(30)]
        public string ContactNumber { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [StringLength(254)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(2000, MinimumLength = 10)]
        public string Message { get; set; } = string.Empty;

        [Range(typeof(bool), "true", "true", ErrorMessage = "Privacy consent is required.")]
        public bool PrivacyAccepted { get; set; }

        [StringLength(200)]
        public string Website { get; set; } = string.Empty;
    }
}
