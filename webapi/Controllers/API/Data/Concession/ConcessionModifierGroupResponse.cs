namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionModifierGroupResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public int MinSelect { get; set; }
        public int? MaxSelect { get; set; }
        public bool IsRequired { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public List<OptionItem> Options { get; set; } = new();

        public class OptionItem
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = null!;
            public int PriceDeltaCents { get; set; }
            public int SortOrder { get; set; }
            public bool IsActive { get; set; }
        }
    }
}
