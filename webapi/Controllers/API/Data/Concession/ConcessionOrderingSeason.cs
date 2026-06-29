namespace webapi.Controllers.API.Data.Concession
{
    // One open-season date range for online ordering. Dates are inclusive, "yyyy-MM-dd", evaluated in
    // the tenant's timezone. A date outside every range is in the closed season.
    public class ConcessionOrderingSeason
    {
        public string StartDate { get; set; } = null!;
        public string EndDate { get; set; } = null!;
    }
}
