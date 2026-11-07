namespace webapi.Controllers.API.Data.Waiver
{
    /// <summary>Signed Waivers: send signing links to people who need an adult waiver.</summary>
    public class BulkAdultWaiverSignRequestRequest
    {
        /// <summary>The people to send to: the Signed Waivers person keys shown on screen. Required; each
        /// is re-checked server side and an empty list sends nothing.</summary>
        public List<string>? PersonKeys { get; set; }
    }
}
