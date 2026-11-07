namespace Services.Repositories.Data.WaiverData
{
    /// <summary>A ticket or pass an account holds that the gate will check next.</summary>
    public class RiderCredentialRef
    {
        public string Kind { get; set; } = "ticket";   // ticket | season_pass
        public Guid Id { get; set; }
        public Guid? EventId { get; set; }
        public DateTime SortAt { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
    }
}
