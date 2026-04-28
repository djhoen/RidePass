namespace webapi.Controllers.API.Data.Blackout
{
    public class BlackoutResponse
    {
        public Guid Id { get; set; }
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public bool AllDay { get; set; }
        public string? Reason { get; set; }
    }
}
