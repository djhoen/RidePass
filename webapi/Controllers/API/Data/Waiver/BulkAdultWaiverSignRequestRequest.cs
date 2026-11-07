namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>Signed Waivers: send signing links to people who need an adult waiver.</summary>
    public class BulkAdultWaiverSignRequestRequest
    {
        /// <summary>Limit the send to these people (Signed Waivers person keys); empty = everyone who
        /// needs an adult waiver.</summary>
        public List<string>? PersonKeys { get; set; }
    }
}
