using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.RiderData;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using Services.Storage;

namespace Services.Riders
{
    /// <summary>Where a rider's photo on file comes from (what the gate shows).</summary>
    public static class RiderPhotoSource
    {
        /// <summary>A photo the venue took (rider_photo), served by the authorized photo endpoint.</summary>
        public const string Rider = "rider";
        /// <summary>The season pass's own registration photo (photo_data_url, already on the pass card).</summary>
        public const string Pass = "pass";
        /// <summary>The account's profile photo (users.image_url), only when the account IS the rider.</summary>
        public const string Account = "account";
    }

    /// <summary>The rider's photo on file, when there is one.</summary>
    public sealed record RiderPhotoOnFile(string Source, Guid? RiderPhotoId, string? Url, DateTime? TakenAtUtc);

    /// <summary>Where a rider's photo is filed: owner + rider key, and the owner's account / email.</summary>
    public sealed record RiderPhotoKey(string OwnerKey, string RiderKey, Guid? UserId, string? OwnerEmail);

    /// <summary>
    /// The ONE rule for "which buying account counts for this tenant": a platform rider account
    /// (tenant_id NULL) or one of this tenant's own; another tenant's staff account never does. Used by
    /// the waiver gate, RiderIdVerification and the rider photo recorder alike.
    /// </summary>
    public static class BuyerAccounts
    {
        public static User? ForTenant(User? account, Guid tenantId) =>
            account is not null && account.Id != Guid.Empty && (account.TenantId is null || account.TenantId == tenantId)
                ? account : null;
    }

    /// <summary>
    /// Pure rules for "does this rider have a photo on file?" (tenant.rider_photo_mode, Script0307),
    /// shared by the gate, the photo endpoints and pass registration so they never disagree about whose
    /// photo it is.
    /// </summary>
    public static class RiderPhotoRules
    {
        public const string CredentialRiderKey = "credential";

        /// <summary>The owner a photo is filed under: the buying account, else (no account) the buyer
        /// email. Null when there is neither.</summary>
        public static string? OwnerKey(Guid? accountId, string? buyerEmail)
        {
            if (accountId is Guid id && id != Guid.Empty) return $"user:{id:D}";
            var email = buyerEmail?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(email) ? null : $"email:{email}";
        }

        /// <summary>The account's profile photo may stand for this rider only when the credential NAMES
        /// the rider and that name is the account owner's (never for a credential naming nobody).</summary>
        public static bool AccountIsNamedRider(User? buyerAccount, string? riderFirst, string? riderLast) =>
            buyerAccount is not null
            && IdCheckRules.Normalize($"{riderFirst} {riderLast}").Length > 0
            && IdCheckRules.AccountIsRider(riderFirst, riderLast, buyerAccount.FirstName, buyerAccount.LastName);

        /// <summary>
        /// Where one credential's rider photo is filed. <paramref name="buyerAccount"/> must already be
        /// tenant-filtered (BuyerAccounts.ForTenant). The rider key is the credential's own rider / holder
        /// name. A TICKET that names nobody is the buyer's own (the buyer is the rider, as the ID check
        /// already keys it): it is filed under the buyer by the account's name, else the buyer's name with
        /// their email, so the photo on file for them (a kiosk photo, an earlier ticket) is found. Only with
        /// no buyer to key by (no account, no email, no name) is it filed under the ticket itself. A PASS
        /// that names nobody is unregistered and stays under the pass ("pass:{id}"): its holder is not
        /// necessarily the buyer, so a parent's photo can never stand in for it.
        /// </summary>
        public static RiderPhotoKey Key(User? buyerAccount, string? buyerEmail, string? riderFirst, string? riderLast,
            string credentialKind, Guid credentialId, string? buyerName = null)
        {
            var owner = OwnerKey(buyerAccount?.Id, buyerAccount is null ? buyerEmail : null);
            var rider = IdCheckRules.Normalize($"{riderFirst} {riderLast}");
            if (rider.Length == 0 && credentialKind == "ticket")
                rider = buyerAccount is not null
                    ? IdCheckRules.Normalize($"{buyerAccount.FirstName} {buyerAccount.LastName}")
                    : IdCheckRules.Normalize(buyerName);
            if (owner is null || rider.Length == 0)
                return new RiderPhotoKey($"{credentialKind}:{credentialId:D}", CredentialRiderKey, null, null);
            return new RiderPhotoKey(owner, rider, buyerAccount?.Id,
                buyerAccount is null ? buyerEmail!.Trim().ToLowerInvariant() : null);
        }

        /// <summary>
        /// Where a credential's rider photo is filed by its rider identity (TicketRiderKeys). Name-keyed
        /// credentials use <see cref="Key"/> exactly as before; a dependent or a rider with their own account
        /// is filed under that keying's account and rider key (else under the credential itself).
        /// <paramref name="keying"/>'s account must already be tenant-filtered.
        /// </summary>
        public static RiderPhotoKey KeyFor(RiderKeying keying, string? buyerEmail, string? riderFirst, string? riderLast,
            string credentialKind, Guid credentialId, string? buyerName = null)
        {
            if (keying.Kind == RiderKeyingKind.Names)
                return Key(keying.Account, buyerEmail, riderFirst, riderLast, credentialKind, credentialId, buyerName);
            if (keying.Account is null || string.IsNullOrEmpty(keying.RiderKey))
                return new RiderPhotoKey($"{credentialKind}:{credentialId:D}", CredentialRiderKey, null, null);
            return new RiderPhotoKey(OwnerKey(keying.Account.Id, null)!, keying.RiderKey, keying.Account.Id, null);
        }

        /// <summary>The keyed account's profile photo may stand for this rider: by name for a name-keyed
        /// credential (<see cref="AccountIsNamedRider"/>), always for the rider's own account, never for a
        /// guardian's account (a dependent).</summary>
        public static bool AccountPhotoIsRiders(RiderKeying keying, string? riderFirst, string? riderLast) =>
            keying.Kind switch
            {
                RiderKeyingKind.Names => AccountIsNamedRider(keying.Account, riderFirst, riderLast),
                RiderKeyingKind.RiderAccount => keying.AccountIsRider && keying.Account is not null,
                _ => false,
            };

        /// <summary>
        /// Priority: the venue's own photo of this rider, else the pass's registration photo (for a
        /// pass holder), else the account's profile photo ONLY when the (tenant-filtered) account is the
        /// rider.
        /// </summary>
        public static RiderPhotoOnFile? Resolve(RiderPhoto? riderPhoto, string? passPhotoDataUrl,
            User? buyerAccount, bool accountIsRider)
        {
            if (riderPhoto is not null)
                return new RiderPhotoOnFile(RiderPhotoSource.Rider, riderPhoto.Id, null,
                    DateTime.SpecifyKind(riderPhoto.TakenAt, DateTimeKind.Utc));
            if (!string.IsNullOrWhiteSpace(passPhotoDataUrl))
                return new RiderPhotoOnFile(RiderPhotoSource.Pass, null, null, null);
            if (accountIsRider && !string.IsNullOrWhiteSpace(buyerAccount?.ImageUrl))
                return new RiderPhotoOnFile(RiderPhotoSource.Account, null, buyerAccount!.ImageUrl, null);
            return null;
        }

        /// <summary>Accepted photo uploads: a JPEG or PNG data URL, the same bounds as a season pass
        /// photo (over ~1 KB, under ~2 MB of base64), whose bytes really are that image type.
        /// Returns the decoded bytes and file extension, or null.</summary>
        public static (byte[] Bytes, string Extension)? DecodePhoto(string? dataUrl)
        {
            if (string.IsNullOrWhiteSpace(dataUrl) || dataUrl.Length is <= 1_000 or >= 2_000_000) return null;
            const string jpeg = "data:image/jpeg;base64,", png = "data:image/png;base64,";
            string ext, b64;
            if (dataUrl.StartsWith(jpeg, StringComparison.Ordinal)) { ext = ".jpg"; b64 = dataUrl[jpeg.Length..]; }
            else if (dataUrl.StartsWith(png, StringComparison.Ordinal)) { ext = ".png"; b64 = dataUrl[png.Length..]; }
            else return null;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); } catch (FormatException) { return null; }
            // Magic numbers: labelled JPEG must start FF D8 FF, PNG 89 'P' 'N' 'G'.
            var isJpeg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            var isPng = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;
            if (ext == ".jpg" ? !isJpeg : !isPng) return null;
            return (bytes, ext);
        }
    }

    /// <summary>Records and reads the venue's gate photos of riders.</summary>
    public interface IRiderPhotos
    {
        /// <summary>The rider photo filed under this key, when its image still exists (a row whose image
        /// is missing counts as no photo, so staff can retake). Memoized per request.</summary>
        Task<RiderPhoto?> Find(Guid tenantId, RiderPhotoKey key);

        /// <summary>Where a ticket's / pass's rider photo is filed (the buyer account tenant-filtered).</summary>
        Task<RiderPhotoKey> KeyForTicket(EventTicketPurchase ticket, Guid tenantId);
        Task<RiderPhotoKey> KeyForPass(SeasonPassPurchase pass, Guid tenantId);

        /// <summary>Saves a photo for the rider a ticket / pass admits (replacing any earlier one).</summary>
        Task<RiderPhoto> RecordForTicket(EventTicketPurchase ticket, Guid tenantId, byte[] image, string extension, Guid? staffUserId);
        Task<RiderPhoto> RecordForPass(SeasonPassPurchase pass, Guid tenantId, byte[] image, string extension, Guid? staffUserId);
        /// <summary>Saves a photo under an explicit key (a kiosk sign-up: the rider's own account, or a
        /// dependent under their guardian, by TicketRiderKeys), so later tickets for that rider find it.</summary>
        Task<RiderPhoto> RecordForKey(Guid tenantId, RiderPhotoKey key, byte[] image, string extension, Guid? staffUserId);
        /// <summary>Deletes one of this tenant's photos (a kiosk photo held for staff, then dismissed or moved).
        /// The image itself is removed by the trash worker.</summary>
        Task<bool> DeletePhoto(Guid photoId, Guid tenantId);

        /// <summary>A gate photo of this pass's holder is on file (pass registration's photo requirement).</summary>
        Task<bool> HolderHasPhoto(Guid passPurchaseId, Guid tenantId);

        /// <summary>The image bytes of one of this tenant's rider photos, or null.</summary>
        Task<(byte[] Bytes, string ContentType)?> Open(Guid photoId, Guid tenantId);
    }

    public class RiderPhotos : IRiderPhotos
    {
        private readonly IRiderPhotoRepository _photos;
        private readonly IUserRepository _users;
        private readonly ISeasonPassRepository _passes;
        private readonly IImageStorage _storage;
        private readonly ITicketRiderIdentity _riderIdentity;
        private readonly Dictionary<(Guid, string, string), RiderPhoto?> _findCache = new();

        public RiderPhotos(IRiderPhotoRepository photos, IUserRepository users, ISeasonPassRepository passes,
            IImageStorage storage, ITicketRiderIdentity riderIdentity)
        {
            _photos = photos;
            _users = users;
            _passes = passes;
            _storage = storage;
            _riderIdentity = riderIdentity;
        }

        public async Task<RiderPhoto?> Find(Guid tenantId, RiderPhotoKey key)
        {
            var k = (tenantId, key.OwnerKey, key.RiderKey);
            if (_findCache.TryGetValue(k, out var hit)) return hit;
            var photo = await _photos.Get(tenantId, key.OwnerKey, key.RiderKey);
            if (photo is not null && !await _storage.PrivateExistsAsync(photo.StorageKey)) photo = null;
            _findCache[k] = photo;
            return photo;
        }

        public async Task<RiderPhotoKey> KeyForTicket(EventTicketPurchase ticket, Guid tenantId)
        {
            if (ticket.RiderUserId is not null || ticket.RiderDependentId is not null)
            {
                // A Gate Sale rider (Script0309): filed by their identity, as the gate reads it.
                var keying = await _riderIdentity.ForTicket(ticket, tenantId);
                return RiderPhotoRules.KeyFor(keying, ticket.PurchaserEmail, ticket.RiderFirstName, ticket.RiderLastName,
                    "ticket", ticket.Id, ticket.PurchaserName);
            }
            // One of several unnamed rider tickets from this buyer for the event: filed under the ticket itself.
            if (await _riderIdentity.IsSharedUnnamed(ticket, tenantId))
                return RiderPhotoRules.KeyFor(TicketRiderKeys.PerTicket(ticket.Id), ticket.PurchaserEmail, null, null, "ticket", ticket.Id);
            var account = ticket.PurchaserUserId is Guid id ? BuyerAccounts.ForTenant(await _users.GetById(id), tenantId) : null;
            return RiderPhotoRules.Key(account, ticket.PurchaserEmail, ticket.RiderFirstName, ticket.RiderLastName,
                "ticket", ticket.Id, ticket.PurchaserName);
        }

        public async Task<RiderPhotoKey> KeyForPass(SeasonPassPurchase pass, Guid tenantId)
        {
            var account = BuyerAccounts.ForTenant(await _users.GetById(pass.PurchaserUserId), tenantId);
            return RiderPhotoRules.Key(account, pass.PurchaserEmail, pass.HolderFirstName, pass.HolderLastName,
                "pass", pass.Id);
        }

        public async Task<RiderPhoto> RecordForTicket(EventTicketPurchase ticket, Guid tenantId, byte[] image,
            string extension, Guid? staffUserId) =>
            await Record(tenantId, await KeyForTicket(ticket, tenantId), image, extension, staffUserId);

        public async Task<RiderPhoto> RecordForPass(SeasonPassPurchase pass, Guid tenantId, byte[] image,
            string extension, Guid? staffUserId) =>
            await Record(tenantId, await KeyForPass(pass, tenantId), image, extension, staffUserId);

        public async Task<bool> DeletePhoto(Guid photoId, Guid tenantId)
        {
            var photo = await _photos.GetById(photoId, tenantId);
            if (photo is null) return false;
            _findCache.Remove((tenantId, photo.OwnerKey, photo.RiderKey));
            return await _photos.Delete(photoId, tenantId) == 1;
        }

        public Task<RiderPhoto> RecordForKey(Guid tenantId, RiderPhotoKey key, byte[] image, string extension,
            Guid? staffUserId) => Record(tenantId, key, image, extension, staffUserId);

        private async Task<RiderPhoto> Record(Guid tenantId, RiderPhotoKey key, byte[] image, string extension,
            Guid? staffUserId)
        {
            using var ms = new MemoryStream(image, writable: false);
            var storageKey = await _storage.SavePrivateAsync(ms, tenantId, "rider-photo", extension);
            RiderPhoto photo;
            string? replaced;
            try
            {
                (photo, replaced) = await _photos.Upsert(tenantId, key.OwnerKey, key.UserId, key.OwnerEmail,
                    key.RiderKey, storageKey, staffUserId);
            }
            catch
            {
                // The row never pointed at the new image: don't leave it behind.
                try { await _storage.DeletePrivateAsync(storageKey); } catch (Exception) { /* best effort */ }
                throw;
            }
            // Committed: the old image is no longer referenced. (The trigger also queued it, so a failure
            // here is retried by PrivateImageTrashWorker.)
            if (!string.IsNullOrEmpty(replaced) && replaced != storageKey)
            {
                try { await _storage.DeletePrivateAsync(replaced); } catch (Exception) { /* queued for the worker */ }
            }
            _findCache.Remove((tenantId, key.OwnerKey, key.RiderKey));
            return photo;
        }

        public async Task<bool> HolderHasPhoto(Guid passPurchaseId, Guid tenantId)
        {
            var pass = await _passes.GetPurchase(passPurchaseId);
            if (pass is null || pass.TenantId != tenantId) return false;
            return await Find(tenantId, await KeyForPass(pass, tenantId)) is not null;
        }

        public async Task<(byte[] Bytes, string ContentType)?> Open(Guid photoId, Guid tenantId)
        {
            var photo = await _photos.GetById(photoId, tenantId);
            // The key must also name this tenant: a row can never serve another tenant's image.
            if (photo is null || PrivateImageKey.TenantOf(photo.StorageKey) != tenantId) return null;
            return await _storage.OpenPrivateAsync(photo.StorageKey);
        }
    }
}
