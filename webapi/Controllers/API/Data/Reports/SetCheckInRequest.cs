namespace webapi.Controllers.API.Data.Reports
{
    public class SetCheckInRequest
    {
        public string Source { get; set; } = null!;   // 'pass' | 'event_ticket' | 'season_pass'
        public bool CheckedIn { get; set; }            // true = check in, false = undo
    }
}
