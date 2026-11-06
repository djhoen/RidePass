namespace webapi.Controllers.API.Data.Cash
{
    // One person's share of a tip jar split.
    public class TipJarShareDto
    {
        public Guid UserId { get; set; }
        public string Name { get; set; } = null!;
        public int AmountCents { get; set; }
    }
}
