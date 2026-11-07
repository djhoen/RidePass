namespace Services.Repositories.Data.RiderData
{
    /// <summary>A private image queued for deletion (private_image_trash, Script0307 trigger).</summary>
    public class PrivateImageTrashItem
    {
        public long Id { get; set; }
        public Guid TenantId { get; set; }
        public string StorageKey { get; set; } = "";
    }
}
