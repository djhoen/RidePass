namespace webapi.Controllers.API.Data.SeasonPass
{
    /// <summary>
    /// A gate worker's record that they checked a rider's photo ID. There is no "I verified it"
    /// boolean on purpose: calling the endpoint IS the attestation, and the date of birth read off
    /// the document is the substance of it. A tick box with nothing behind it is what
    /// tenant.require_id_at_checkin already does, and it is precisely what this replaces.
    /// </summary>
    public class VerifyRiderIdRequest
    {
        /// <summary>
        /// The date of birth printed on the ID. Null falls back to the birthdate the rider gave at
        /// registration, which covers the common case where the document simply confirms it; the
        /// server rejects the request when neither is available.
        /// </summary>
        public DateTime? VerifiedDob { get; set; }
    }
}
