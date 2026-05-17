namespace webapi.Controllers.API.Data.Waiver
{
    public class WaiverEventAssociationResponse
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }
        public bool AsRider { get; set; }
        public bool AsSpectator { get; set; }
    }
}
