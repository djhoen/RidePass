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

    /// <summary>
    /// Pure rules for "does this rider have a photo on file?" (tenant.rider_photo_mode, Script0307),
    /// shared by the gate and the photo endpoints so they never disagree about whose photo it is.
    /// </summary>
    public static class RiderPhotoRules
    {
        /// <summary>The owner a photo is filed under: the buying account, else (no account) the buyer
        /// email. Null when there is neither.</summary>
        public static string? OwnerKey(Guid? accountId, string? buyerEmail)
        {
            if (accountId is Guid id && id != Guid.Empty) return $"user:{id:D}";
            var email = buyerEmail?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(email) ? null : $"email:{email}";
        }

        /// <summary>The rider under that owner: the credential's rider / holder name, or the account
        /// owner's own name when it names nobody (IdCheckRules.RiderKey, the item-3 key).</summary>
        public static string RiderKey(string? riderFirst, string? riderLast, User? account) =>
            IdCheckRules.RiderKey(riderFirst, riderLast, account?.FirstName, account?.LastName);

        /// <summary>
        /// Priority: the venue's own photo of this rider, else the pass's registration photo (for a
        /// pass holder), else the account's profile photo ONLY when the account is the rider.
        /// </summary>
        public static RiderPhotoOnFile? Resolve(RiderPhoto? riderPhoto, string? passPhotoDataUrl,
            User? account, bool accountIsRider)
        {
            if (riderPhoto is not null)
                return new RiderPhotoOnFile(RiderPhotoSource.Rider, riderPhoto.Id, null,
                    DateTime.SpecifyKind(riderPhoto.TakenAt, DateTimeKind.Utc));
            if (!string.IsNullOrWhiteSpace(passPhotoDataUrl))
                return new RiderPhotoOnFile(RiderPhotoSource.Pass, null, null, null);
            if (accountIsRider && !string.IsNullOrWhiteSpace(account?.ImageUrl))
                return new RiderPhotoOnFile(RiderPhotoSource.Account, null, account!.ImageUrl, null);
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
        /// <summary>Saves a photo for the rider a ticket admits (replacing any earlier one). Null when the
        /// ticket has neither an account nor a buyer email to file it under.</summary>
        Task<RiderPhoto?> RecordForTicket(EventTicketPurchase ticket, Guid tenantId, byte[] image, string extension, Guid? staffUserId);
        Task<RiderPhoto?> RecordForPass(SeasonPassPurchase pass, Guid tenantId, byte[] image, string extension, Guid? staffUserId);
        /// <summary>The image bytes of one of this tenant's rider photos, or null.</summary>
        Task<(byte[] Bytes, string ContentType)?> Open(Guid photoId, Guid tenantId);
    }

    public class RiderPhotos : IRiderPhotos
    {
        private readonly IRiderPhotoRepository _photos;
        private readonly IUserRepository _users;
        private readonly IImageStorage _storage;

        public RiderPhotos(IRiderPhotoRepository photos, IUserRepository users, IImageStorage storage)
        {
            _photos = photos;
            _users = users;
            _storage = storage;
        }

        public async Task<RiderPhoto?> RecordForTicket(EventTicketPurchase ticket, Guid tenantId, byte[] image,
            string extension, Guid? staffUserId)
        {
            var account = ticket.PurchaserUserId is Guid id ? await BuyerAccount(id, tenantId) : null;
            return await Record(tenantId, account, ticket.PurchaserEmail, ticket.RiderFirstName, ticket.RiderLastName,
                image, extension, staffUserId);
        }

        public async Task<RiderPhoto?> RecordForPass(SeasonPassPurchase pass, Guid tenantId, byte[] image,
            string extension, Guid? staffUserId)
        {
            var account = await BuyerAccount(pass.PurchaserUserId, tenantId);
            return await Record(tenantId, account, pass.PurchaserEmail, pass.HolderFirstName, pass.HolderLastName,
                image, extension, staffUserId);
        }

        private async Task<RiderPhoto?> Record(Guid tenantId, User? account, string? buyerEmail,
            string? riderFirst, string? riderLast, byte[] image, string extension, Guid? staffUserId)
        {
            var owner = RiderPhotoRules.OwnerKey(account?.Id, account is null ? buyerEmail : null);
            if (owner is null) return null;
            var riderKey = RiderPhotoRules.RiderKey(riderFirst, riderLast, account);
            if (riderKey.Length == 0) return null;

            using var ms = new MemoryStream(image);
            var key = await _storage.SavePrivateAsync(ms, tenantId, "rider-photo", extension);
            var (photo, replaced) = await _photos.Upsert(tenantId, owner, account?.Id,
                account is null ? buyerEmail?.Trim().ToLowerInvariant() : null, riderKey, key, staffUserId);
            if (!string.IsNullOrEmpty(replaced) && replaced != key)
            {
                try { await _storage.DeletePrivateAsync(replaced); }
                catch (Exception) { /* the row already points at the new image; an orphaned file is harmless */ }
            }
            return photo;
        }

        public async Task<(byte[] Bytes, string ContentType)?> Open(Guid photoId, Guid tenantId)
        {
            var photo = await _photos.GetById(photoId, tenantId);
            // The key must also name this tenant: a row can never serve another tenant's image.
            if (photo is null || PrivateImageKey.TenantOf(photo.StorageKey) != tenantId) return null;
            return await _storage.OpenPrivateAsync(photo.StorageKey);
        }

        // Same rule as RiderIdVerification's buyer account: a platform rider account, or one of this tenant's.
        private async Task<User?> BuyerAccount(Guid id, Guid tenantId)
        {
            if (id == Guid.Empty) return null;
            var u = await _users.GetById(id);
            return u is not null && (u.TenantId is null || u.TenantId == tenantId) ? u : null;
        }
    }
}
