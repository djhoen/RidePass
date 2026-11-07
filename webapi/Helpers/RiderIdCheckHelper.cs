using Services.Riders;

namespace webapi.Helpers
{
    /// <summary>
    /// What the two "staff checked this rider's ID" endpoints (season pass and order ticket) share:
    /// validating the date of birth read off the document, and the rider.id_verified audit entry.
    /// </summary>
    public static class RiderIdCheckHelper
    {
        /// <summary>The DOB to record: the one typed from the ID, else the rider's own (the ID simply
        /// confirmed it). Returns an error message instead when there is none or it is in the future.</summary>
        public static (DateTime? Dob, string? Error) ResolveDob(DateTime? fromId, DateTime? onFile)
        {
            var dob = fromId ?? onFile;
            if (dob is null)
                return (null, "Enter the date of birth from the rider's ID. It's what the age check rests on.");
            if (dob.Value.Date > DateTime.UtcNow.Date)
                return (null, "That date of birth is in the future. Check the ID again.");
            return (dob.Value.Date, null);
        }

        /// <summary>True when the ID's DOB differs from the one the rider gave.</summary>
        public static bool Differs(DateTime dob, DateTime? onFile) => onFile is { } f && f.Date != dob.Date;

        public static Task Audit(Services.Audit.IAuditLogger audit, Guid tenantId, string riderName,
            RiderIdVerificationStatus status, DateTime dob, DateTime? onFile, string targetKind, Guid targetId) =>
            audit.Log("rider.id_verified",
                $"Verified ID and age for {riderName} (age {status.VerifiedAge}).",
                targetKind: targetKind, targetId: targetId, tenantId: tenantId,
                metadata: new { scope = status.Scope, verifiedDob = dob.Date,
                    recordedBirthdate = onFile?.Date, dobMismatch = Differs(dob, onFile) });
    }
}
