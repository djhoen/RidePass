using webapi.Controllers.API.Data.SuperAdmin;

namespace webapi.Staging
{
    /// <summary>
    /// Orchestrates a one-way "copy production down to staging" refresh (dump prod,
    /// restore into the stage DB, sanitize). Available ONLY on the staging environment;
    /// a no-op/forbidden everywhere else. Runs as a single background job at a time.
    /// </summary>
    public interface IStageMirrorService
    {
        /// <summary>True only when running on Staging with StageMirror:Enabled = true.</summary>
        bool Available { get; }

        /// <summary>Current job state (and Available flag) for the UI.</summary>
        StageMirrorStatus Snapshot();

        /// <summary>
        /// Kick off a refresh in the background. Returns (false, reason) if unavailable,
        /// misconfigured, or a run is already in progress.
        /// </summary>
        (bool started, string? error) Start(string startedBy);
    }
}
