using System.ComponentModel.DataAnnotations;

namespace webapi.Controllers.API.Data.Spectator
{
    public class SpectatorBuyRequest
    {
        [Required] public Guid EventId { get; set; }
        [Required, EmailAddress, MaxLength(200)] public string PurchaserEmail { get; set; } = null!;
        [Required, MaxLength(120)] public string PurchaserName { get; set; } = null!;

        // The Gate Fee / spectator add-on products being bought, with quantities.
        [Required, MinLength(1)] public List<SpectatorBuyItem> Items { get; set; } = new();

        // One entry per spectator attending. Quantity-of-units must equal Spectators.Count
        // when the event's spectator waiver is active. Each spectator is an attendee that
        // needs a waiver on file (or an inline signature provided here).
        public List<SpectatorEntry> Spectators { get; set; } = new();
    }

    public class SpectatorBuyItem
    {
        [Required] public Guid ProductId { get; set; }
        [Range(1, 50)] public int Quantity { get; set; } = 1;
        public Guid? VariantId { get; set; }
    }

    public class SpectatorEntry
    {
        [Required, MaxLength(80)] public string FirstName { get; set; } = null!;
        [Required, MaxLength(80)] public string LastName { get; set; } = null!;
        [Required] public DateTime Birthdate { get; set; }

        // Required when the event's spectator waiver demands signing AND the purchaser
        // hasn't already signed for THIS attendee (themselves or a specific child).
        // Data URL of a PNG sketch.
        public string? SignatureDataUrl { get; set; }

        // For minors: parent / guardian who is signing on the spectator's behalf.
        // Inferred from birthdate vs. 18-year cutoff at validation time.
        [MaxLength(120)] public string? ParentName { get; set; }
        [MaxLength(40)] public string? ParentPhone { get; set; }
    }

    public class SpectatorBuyResponse
    {
        public List<Guid> PurchaseIds { get; set; } = new();
        public string ClientSecret { get; set; } = null!;
        public int AmountCents { get; set; }
    }

    public class CheckSignatureResponse
    {
        public bool HasSigned { get; set; }
    }
}
