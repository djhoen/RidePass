namespace webapi.Controllers.API.Data.LessonChange
{
    public class LessonTokenRequest
    {
        /// <summary>The ticket's redemption token, from the emailed link, when the rider isn't signed in.</summary>
        public Guid? Token { get; set; }
    }
}
