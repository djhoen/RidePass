namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>What the public /SignWaiver/{token} page needs to render.</summary>
    public class PublicWaiverSignRequestResponse
    {
        public string Status { get; set; } = "pending";
        public string? RecipientName { get; set; }
        public string RecipientEmail { get; set; } = string.Empty;
        public Guid WaiverId { get; set; }
        public string WaiverName { get; set; } = string.Empty;
        public string WaiverTitle { get; set; } = string.Empty;
        public string WaiverBody { get; set; } = string.Empty;
        public int WaiverVersion { get; set; }
        public bool AlreadySigned { get; set; }

        /// <summary>For a link sent for a specific rider (a gate link, or an adult-waiver link), how it
        /// must be signed, as the server will enforce it: "guardian" (a parent / guardian signs for the
        /// rider), "adult" (the rider signs as themselves), or null when the server needs the birthdate
        /// typed on the form to decide (an untargeted link, or a rider whose birthdate isn't known).</summary>
        public string? SignAs { get; set; }
        /// <summary>The rider the link is for (the name the signature is recorded under).</summary>
        public string? RiderName { get; set; }
        public string? RiderFirstName { get; set; }
        public string? RiderLastName { get; set; }
    }
}
