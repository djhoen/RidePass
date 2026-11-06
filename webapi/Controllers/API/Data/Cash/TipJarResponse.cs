namespace webapi.Controllers.API.Data.Cash
{
    // The tip jar for one business date (GET Cash/TipJar): who closed a shift that day (the
    // people pre-checked in the split dialog), every counter staff member who could take a
    // share, and the splits already recorded.
    public class TipJarResponse
    {
        // yyyy-MM-dd
        public string Date { get; set; } = null!;
        public List<Guid> SuggestedUserIds { get; set; } = new();
        public List<TipJarStaffDto> Staff { get; set; } = new();
        public List<TipJarSplitDto> Splits { get; set; } = new();
    }
}
