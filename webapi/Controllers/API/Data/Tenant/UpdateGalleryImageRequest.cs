namespace webapi.Controllers.API.Data.Tenant
{
    public class UpdateGalleryImageRequest
    {
        public string? Caption { get; set; }
        public int SortOrder { get; set; }
    }

    public class UpdateTrackGraphicRequest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public int SortOrder { get; set; }
    }
}
