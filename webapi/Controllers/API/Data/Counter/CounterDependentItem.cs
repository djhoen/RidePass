namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>A minor already linked to a guardian's account, for picking at the counter.</summary>
    public class CounterDependentItem
    {
        public Guid Id { get; set; }
        public Guid GuardianUserId { get; set; }
        public Guid? RiderUserId { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public DateOnly? Birthdate { get; set; }
        public bool IsMinor { get; set; }
    }
}
