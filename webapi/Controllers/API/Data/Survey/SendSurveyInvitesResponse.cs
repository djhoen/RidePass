namespace webapi.Controllers.API.Data.Survey
{
    public class SendSurveyInvitesResponse
    {
        public int Sent { get; set; }
        public int Skipped { get; set; }      // duplicates / invalid addresses
        public List<string> SkippedEmails { get; set; } = new();
    }
}
