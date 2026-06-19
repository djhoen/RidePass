using System.Diagnostics;
using System.Text;
using webapi.Controllers.API.Data.SuperAdmin;

namespace webapi.Staging
{
    public class StageMirrorService : IStageMirrorService
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<StageMirrorService> _logger;

        private readonly object _lock = new();
        private bool _running;
        private string _state = "idle";
        private DateTime? _startedAtUtc;
        private DateTime? _finishedAtUtc;
        private string? _startedBy;
        private readonly StringBuilder _log = new();
        private const int MaxLogChars = 32 * 1024;   // keep the tail; this is shown to admins

        public StageMirrorService(IConfiguration config, IWebHostEnvironment env, ILogger<StageMirrorService> logger)
        {
            _config = config;
            _env = env;
            _logger = logger;
        }

        // Hard gate: must be the Staging environment AND explicitly enabled in config.
        // On production env.IsStaging() is false, so this is always off there.
        public bool Available =>
            _env.IsStaging() &&
            bool.TryParse(_config["StageMirror:Enabled"], out var on) && on;

        public StageMirrorStatus Snapshot()
        {
            lock (_lock)
            {
                return new StageMirrorStatus
                {
                    Available = Available,
                    State = _state,
                    StartedAtUtc = _startedAtUtc,
                    FinishedAtUtc = _finishedAtUtc,
                    StartedBy = _startedBy,
                    Log = _log.Length == 0 ? null : _log.ToString(),
                };
            }
        }

        public (bool started, string? error) Start(string startedBy)
        {
            if (!Available)
            {
                return (false, "Staging mirror is not available in this environment.");
            }

            var source = _config["StageMirror:SourceUrl"];
            var target = _config["StageMirror:TargetUrl"];
            var scriptPath = _config["StageMirror:ScriptPath"] ?? "/var/www/staging/scripts/refresh-stage-db.sh";

            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                return (false, "StageMirror:SourceUrl and StageMirror:TargetUrl must be configured.");
            }
            // Defense in depth: never let the restore target be anything but a staging DB.
            if (!target.Contains("stage", StringComparison.OrdinalIgnoreCase))
            {
                return (false, "Refusing to run: StageMirror:TargetUrl does not look like a staging database.");
            }
            if (!File.Exists(scriptPath))
            {
                return (false, $"Refresh script not found at {scriptPath}.");
            }

            lock (_lock)
            {
                if (_running) return (false, "A refresh is already running.");
                _running = true;
                _state = "running";
                _startedAtUtc = DateTime.UtcNow;
                _finishedAtUtc = null;
                _startedBy = startedBy;
                _log.Clear();
            }

            // Fire-and-forget background run; status is polled via Snapshot().
            _ = Task.Run(() => RunAsync(scriptPath, source!, target!));
            return (true, null);
        }

        private async Task RunAsync(string scriptPath, string source, string target)
        {
            try
            {
                Append($"Starting staging refresh ({DateTime.UtcNow:u})...");
                var psi = new ProcessStartInfo
                {
                    FileName = "bash",
                    ArgumentList = { scriptPath },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                // Credentials are passed via env only (never logged).
                psi.Environment["PROD_DB_URL"] = source;
                psi.Environment["STAGE_DB_URL"] = target;

                using var proc = new Process { StartInfo = psi };
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) Append(e.Data); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                await proc.WaitForExitAsync();

                var ok = proc.ExitCode == 0;
                Append(ok ? "Refresh completed successfully." : $"Refresh failed (exit code {proc.ExitCode}).");
                Finish(ok ? "succeeded" : "failed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stage mirror refresh threw.");
                Append($"Refresh errored: {ex.Message}");
                Finish("failed");
            }
        }

        private void Append(string line)
        {
            lock (_lock)
            {
                _log.AppendLine(line);
                if (_log.Length > MaxLogChars)
                {
                    _log.Remove(0, _log.Length - MaxLogChars);
                }
            }
        }

        private void Finish(string state)
        {
            lock (_lock)
            {
                _state = state;
                _finishedAtUtc = DateTime.UtcNow;
                _running = false;
            }
        }
    }
}
