namespace webapi.Controllers.API.Data.Survey
{
    public class SurveyResultsResponse
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = null!;
        public string Status { get; set; } = null!;
        public int ResponseCount { get; set; }
        public int InviteSent { get; set; }
        public int InviteOpened { get; set; }
        public int InviteCompleted { get; set; }
        public List<SurveyQuestionResult> Questions { get; set; } = new();
    }
}
