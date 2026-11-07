namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>Result of sending adult-waiver links to the people shown under "Needs adult waiver".</summary>
    public class AdultWaiverBulkResponse
    {
        /// <summary>Links created (at most the per-send cap).</summary>
        public int Created { get; set; }
        /// <summary>Of those, links aimed at an upcoming ticket or pass (the rest at the active waiver).</summary>
        public int TargetedCredential { get; set; }
        /// <summary>Links emailed.</summary>
        public int Sent { get; set; }
        /// <summary>Links created whose email failed (the link can be copied from Waiver requests).</summary>
        public int EmailFailures { get; set; }
        /// <summary>No email server: links were created but not emailed.</summary>
        public bool EmailNotConfigured { get; set; }
        /// <summary>Skipped: no email of the rider's own (only a signer's / guardian's address, or none).</summary>
        public int SkippedNoRiderEmail { get; set; }
        /// <summary>Skipped: this rider already has an open adult-waiver link.</summary>
        public int SkippedOpenLink { get; set; }
        /// <summary>Selected but no longer needing an adult waiver (re-checked server side).</summary>
        public int NoLongerNeeded { get; set; }
        /// <summary>Eligible but over the per-send cap; send again to reach them.</summary>
        public int Deferred { get; set; }
    }
}
