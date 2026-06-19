namespace webapi.Controllers.API.Data.SuperAdmin
{
    /// <summary>
    /// State of the "refresh staging from production" job. Surfaced to the super-admin
    /// Misc settings page so the button + progress can render (only when Available).
    /// </summary>
    public class StageMirrorStatus
    {
        // True only on the staging environment with the feature enabled. The button/
        // endpoint are inert everywhere else (and absent entirely on production).
        public bool Available { get; set; }
        // idle | running | succeeded | failed
        public string State { get; set; } = "idle";
        public System.DateTime? StartedAtUtc { get; set; }
        public System.DateTime? FinishedAtUtc { get; set; }
        public string? StartedBy { get; set; }
        public string? Log { get; set; }
    }
}
