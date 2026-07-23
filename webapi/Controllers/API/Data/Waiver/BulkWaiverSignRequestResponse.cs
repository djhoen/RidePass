namespace webapi.Controllers.API.Data.Waiver
{
    public class BulkWaiverSignRequestResponse
    {
        /// <summary>Requests created and emailed.</summary>
        public int Created { get; set; }
        /// <summary>Roster members skipped: already covered by a current waiver or an open request.</summary>
        public int AlreadyCovered { get; set; }
        /// <summary>Requests created but whose email failed to send (link can be copied manually).</summary>
        public int EmailFailures { get; set; }
    }
}
