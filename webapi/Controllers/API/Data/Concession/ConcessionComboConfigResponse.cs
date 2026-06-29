namespace webapi.Controllers.API.Data.Concession
{
    // The shared "make it a combo" definition: size tiers + choose-one slots with their component options.
    // Drives both the admin editor and the build modal on the POS / online menu.
    public class ConcessionComboConfigResponse
    {
        public List<Tier> Tiers { get; set; } = new();
        public List<Slot> Slots { get; set; } = new();

        public class Tier
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = null!;
            public string? SizeLabel { get; set; }   // matches a component variant's size (e.g. "Large")
            public int PriceCents { get; set; }       // upcharge added to the entree
            public int SortOrder { get; set; }
        }

        public class Slot
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = null!;
            public bool IsRequired { get; set; }
            public int SortOrder { get; set; }
            public List<Option> Options { get; set; } = new();
        }

        public class Option
        {
            public Guid Id { get; set; }
            public Guid ComponentProductId { get; set; }
            public string ComponentName { get; set; } = null!;
            public bool IsDefault { get; set; }   // the included choice; subs are priced vs this one
            public int SortOrder { get; set; }
        }
    }
}
