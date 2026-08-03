namespace webapi.Controllers.API.Data.Concession
{
    public class ConcessionDisplayResponse
    {
        public Guid Id { get; set; }
        public string PairCode { get; set; } = null!;
        public string? StateJson { get; set; }
        public int? TipCents { get; set; }
    }
}
