namespace Services.Repositories.Data.PaymentData
{
    /// <summary>Minimal tenant-scoped view of a reservation for wristband-link validation: is the
    /// admission actually checked in, and what scope should a band linked to it inherit (the
    /// event when one ran, otherwise the tenant-local calendar date)?</summary>
    public class SeasonPassReservationLinkContext
    {
        public string Status { get; set; } = null!;
        public Guid? EventId { get; set; }
        public DateOnly? CheckInDate { get; set; }
        public string PurchaserName { get; set; } = null!;
        // The pass behind the admission, so a band-issuing gate can check what the tenant requires
        // of the HOLDER (a verified ID) before the band goes on their wrist. No waiver field: the
        // waiver is settled at admission, and this context only ever describes a checked_in row.
        public Guid SeasonPassPurchaseId { get; set; }
        public string? HolderFirstName { get; set; }
        public string? HolderLastName { get; set; }
        /// <summary>Best name for the person being banded: the registered holder, else the buyer.</summary>
        public string HolderDisplayName =>
            string.IsNullOrWhiteSpace(HolderFirstName)
                ? PurchaserName
                : $"{HolderFirstName} {HolderLastName}".Trim();
    }
}
