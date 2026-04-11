namespace Services.Repositories.Data.SiteData
{
    public class Banner
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Text { get; set; }
        public string? ActionUrl { get; set; }
        public bool IsActive { get; set; }
        public string? Class { get; set; }
    }

    public class Setting
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string? Value { get; set; }
        public string? Category { get; set; }
    }
}
