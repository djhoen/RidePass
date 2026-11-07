using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using Services.Waivers;

namespace webapi.Helpers
{
    /// <summary>
    /// Signing a waiver at the venue, shared by the Gate Sale (the customer's account waiver) and the kiosk,
    /// so both enforce the same guardian rules server-side:
    /// - on an account: a minor (by the gate's birthdate source, on the tenant's date) needs a parent or
    ///   guardian's name and phone, and the signature is the parent's; an adult over a minor-era signature
    ///   signs again as an adult (the old row is kept) and their credentials are relinked;
    /// - for a child without an account (a dependent): a registrant signature naming the child, always by
    ///   the parent or guardian (the signing-link path, SignByToken).
    /// </summary>
    public static class AccountWaiverSigning
    {
        public const string MinorNeedsGuardian = "Riders under 18 need a parent or guardian's name and phone number on the waiver.";

        public static async Task<(Guid? SignatureId, string? Error)> SignForAccount(IWaiverRepository waivers,
            IWaiverCheckInGate gate, Guid tenantId, string? timezone, User account, Guid waiverId, string? ip,
            string signatureDataUrl, string? parentName, string? parentPhone)
        {
            var existing = await waivers.GetSignature(account.Id, waiverId);
            // A guardian / pre-18 signature stops counting once the rider is 18: they sign again as an
            // adult (Sign keeps the old row as the record).
            var supersede = existing is not null
                && AccountWaiverStatus.IsMinorEraForAdult(account, existing, timezone, DateTime.UtcNow);
            var isMinor = AccountWaiverStatus.IsMinorToday(account, timezone, DateTime.UtcNow);
            string? pName = null, pPhone = null;
            if (isMinor)
            {
                pName = string.IsNullOrWhiteSpace(parentName) ? null : parentName.Trim();
                pPhone = string.IsNullOrWhiteSpace(parentPhone) ? null : parentPhone.Trim();
                if (pName is null || pPhone is null || pPhone.Length < 7) return (null, MinorNeedsGuardian);
            }
            // An adult signing for themselves records who it is for (their name and birthdate), so the gate
            // recognises it on their tickets and passes.
            var sigId = await waivers.Sign(tenantId, account.Id, waiverId, ip, signatureDataUrl,
                signedByParent: isMinor, parentName: pName, parentPhone: pPhone, supersede: supersede,
                attendeeFirstName: isMinor ? null : account.FirstName, attendeeLastName: isMinor ? null : account.LastName,
                attendeeBirthdate: isMinor ? null : AccountWaiverStatus.Birthdate(account)?.ToDateTime(TimeOnly.MinValue));
            // Re-signed as an adult over a minor-era signature: their own credentials still pointing at the
            // old one are relinked (gate linking rules).
            if (supersede) await gate.RelinkAfterAdultResign(tenantId, account.Id, sigId, waiverId);
            return (sigId, null);
        }

        public static async Task<(Guid? SignatureId, string? Error)> SignForDependent(IWaiverRepository waivers,
            Guid tenantId, Guid waiverId, string? ip, string signatureDataUrl, string childFirstName, string childLastName,
            DateOnly childBirthdate, string? guardianName, string? guardianPhone, string? guardianEmail)
        {
            var pName = string.IsNullOrWhiteSpace(guardianName) ? null : guardianName.Trim();
            var pPhone = string.IsNullOrWhiteSpace(guardianPhone) ? null : guardianPhone.Trim();
            if (pName is null || pPhone is null || pPhone.Length < 7) return (null, MinorNeedsGuardian);
            var sigId = await waivers.SignRegistrant(tenantId, waiverId, ip, signatureDataUrl,
                signerEmail: guardianEmail, signerName: pName,
                attendeeFirstName: childFirstName, attendeeLastName: childLastName,
                attendeeBirthdate: childBirthdate.ToDateTime(TimeOnly.MinValue),
                signedByParent: true, parentName: pName, parentPhone: pPhone);
            return (sigId, null);
        }
    }
}
