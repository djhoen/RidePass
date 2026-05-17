using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Feedback
{
    public class SubmitFeedbackRequest
    {
        [Required, MaxLength(120)]
        public string Name { get; set; } = null!;

        [Required, EmailAddress, MaxLength(200)]
        public string Email { get; set; } = null!;

        // Optional 1..5 star rating.
        [Range(1, 5)]
        public int? Rating { get; set; }

        [Required, MinLength(1), MaxLength(4000)]
        public string Body { get; set; } = null!;
    }

    public class FeedbackResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string Email { get; set; } = null!;
        public int? Rating { get; set; }
        public string Body { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? AdminNotes { get; set; }
        public Guid? UserId { get; set; }
        public Guid? ActionedByUserId { get; set; }
        public DateTime? ActionedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }

    public class FeedbackListResponse
    {
        public List<FeedbackResponse> Items { get; set; } = new();
        public int Total { get; set; }
    }

    public class UpdateFeedbackStatusRequest
    {
        [Required, RegularExpression("^(new|addressed|dismissed)$")]
        public string Status { get; set; } = null!;

        [MaxLength(2000)]
        public string? AdminNotes { get; set; }
    }
}
