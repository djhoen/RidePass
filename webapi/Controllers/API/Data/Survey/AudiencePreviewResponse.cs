namespace webapi.Controllers.API.Data.Survey
{
    public class AudiencePreviewResponse
    {
        public int Count { get; set; }
        // First 10 valid recipient emails. Lets the admin sanity-check before
        // hitting Send.
        public List<string> Sample { get; set; } = new();
    }
}
