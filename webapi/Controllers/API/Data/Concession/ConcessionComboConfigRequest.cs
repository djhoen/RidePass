namespace webapi.Controllers.API.Data.Concession
{
    // Admin payload to replace the tenant's "make it a combo" definition (tiers + slots) wholesale.
    public class ConcessionComboConfigRequest
    {
        public List<Tier> Tiers { get; set; } = new();
        public List<Slot> Slots { get; set; } = new();

        public class Tier
        {
            public string Name { get; set; } = null!;
            public string? SizeLabel { get; set; }
            public int PriceCents { get; set; }
        }

        public class Slot
        {
            public string Name { get; set; } = null!;
            public bool IsRequired { get; set; } = true;
            public List<Option> Options { get; set; } = new();
        }

        public class Option
        {
            public Guid ComponentProductId { get; set; }
            public bool IsDefault { get; set; }
        }
    }
}
