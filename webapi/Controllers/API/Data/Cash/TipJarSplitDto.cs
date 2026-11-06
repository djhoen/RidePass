namespace webapi.Controllers.API.Data.Cash
{
    // One recorded tip jar split and its shares.
    public class TipJarSplitDto
    {
        public Guid Id { get; set; }
        public int TotalCents { get; set; }
        public string? Note { get; set; }
        public string CreatedByName { get; set; } = null!;
        public DateTime CreatedAtUtc { get; set; }
        public List<TipJarShareDto> Shares { get; set; } = new();
    }
}
