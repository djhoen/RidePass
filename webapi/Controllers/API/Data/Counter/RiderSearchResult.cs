namespace webapi.Controllers.API.Data.Counter
{
    /// <summary>
    /// One candidate from a counter customer search. Deliberately thin: enough for the cashier to
    /// tell two people apart in a queue and pick the right one, and nothing more. The full record
    /// (waiver state, emergency contact) is fetched by the existing exact lookup once they choose,
    /// so a broad search never sprays personal detail across a list.
    /// </summary>
    public class RiderSearchResult
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = null!;
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        /// <summary>Shown so an operator can disambiguate two people with the same name.</summary>
        public string? Phone { get; set; }
    }
}
