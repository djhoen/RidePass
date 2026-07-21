using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.BikeShop
{
    public class UpsertShopWorkOrderRequest
    {
        [Required, MaxLength(160)] public string CustomerName { get; set; } = null!;
        [MaxLength(40)] public string? CustomerPhone { get; set; }
        [MaxLength(200)] public string? CustomerEmail { get; set; }
        public Guid? CustomerUserId { get; set; }

        // The subject: the shop's own unit (fleet service) or the customer's bike as free text.
        public Guid? SubjectItemId { get; set; }
        [MaxLength(300)] public string? CustomerBikeDesc { get; set; }
        /// <summary>The customer's bike record. Preferred; CustomerBikeDesc remains the fallback.</summary>
        public Guid? CustomerBikeId { get; set; }

        // The status code (built-in or a tenant's custom one). Validated in the controller against
        // the tenant's active statuses, since custom codes can't be a fixed regex.
        [Required, MaxLength(40)] public string Status { get; set; } = "intake";
        public Guid? AssignedTechUserId { get; set; }
        /// <summary>Attach this new ticket to an existing customer visit (only honored on create,
        /// and only when the group already belongs to the tenant).</summary>
        public Guid? GroupId { get; set; }
        [MaxLength(4000)] public string? IntakeNotes { get; set; }
        /// <summary>Customer-facing note printed on the claim tag and the bill.</summary>
        [MaxLength(2000)] public string? CustomerNotes { get; set; }
        public DateTime? PromisedAt { get; set; }
    }
}
