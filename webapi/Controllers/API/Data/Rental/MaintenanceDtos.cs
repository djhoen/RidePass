using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Rental
{
    public class UpsertMaintenanceRequest
    {
        [Required] public DateTime StartsAtDate { get; set; }
        [Required] public DateTime EndsAtDate { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
    }

    public class MaintenanceResponse
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }
        public DateTime StartsAtDate { get; set; }
        public DateTime EndsAtDate { get; set; }
        public string? Reason { get; set; }
    }

    public class PerItemConditionInput
    {
        [Required] public Guid PurchaseItemId { get; set; }

        // Base64 data-url (data:image/jpeg;base64,...). Length-checked in code.
        public string? PhotoDataUrl { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }

    /// <summary>Counter MarkOut payload — optional per-unit photos + notes.</summary>
    public class MarkOutRequest
    {
        public List<PerItemConditionInput>? Items { get; set; }
    }

    /// <summary>Counter MarkReturned payload — adds deposit-capture amount + per-unit return condition.</summary>
    public class MarkReturnedRequest
    {
        public string? ConditionNotes { get; set; }
        public int DepositCapturedCents { get; set; }
        public List<PerItemConditionInput>? Items { get; set; }
    }
}
