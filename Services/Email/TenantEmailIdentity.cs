using Services.Helpers;
using Services.Repositories.Data.TenantData;

namespace Services.Email
{
    /// <summary>
    /// The single place that decides how a tenant appears in a rider's inbox.
    ///
    /// Riders have a relationship with the TRACK, not with RidePass: they bought a race entry from
    /// Motoland, so mail about it has to say Motoland, or it reads as spam from a company they've
    /// never heard of. The From address stays the platform's authenticated noreply@ (we're
    /// DKIM-signed for ridepass.io, and signing as a track's own domain would need that track to
    /// publish DNS records), so the tenant's identity rides on the display name and the Reply-To.
    ///
    /// Change the format here and every tenant-originated email follows.
    /// </summary>
    public static class TenantEmailIdentity
    {
        /// <summary>A null tenant (platform-level mail, or a tenant row we couldn't load) returns null,
        /// which falls back to the platform sender rather than sending as nobody.</summary>
        public static EmailSender? For(Tenant? tenant) =>
            tenant is null
                ? null
                : new EmailSender(
                    FromName: tenant.DisplayName,
                    ReplyToEmail: tenant.ContactEmail,
                    ReplyToName: tenant.DisplayName);
    }
}
