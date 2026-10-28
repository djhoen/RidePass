using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.QuickBooks.Desktop;
using Services.Repositories.Data.QuickBooksData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.QuickBooks;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// QuickBooks Desktop: switch a track onto Desktop, take its Chart of Accounts and Class List
    /// from a Desktop export, and hand out IIF files of daily journal entries. The edition-neutral
    /// screens (status, mapping, classes, history) stay on QuickBooksController, which dispatches on
    /// edition, so the settings page talks to one API whichever edition the track uses.
    ///
    /// A track is on Online OR Desktop, never both: two live destinations for the same days would
    /// make "never post a day twice" depend on the bookkeeper remembering which one they used.
    /// </summary>
    [ApiController]
    [Route("api/QuickBooks/Desktop")]
    public class QuickBooksDesktopController : ControllerBase
    {
        /// <summary>A real chart of accounts plus class list is tens of KB; this is generous.</summary>
        private const long MaxListFileBytes = 5 * 1024 * 1024;

        private readonly IQuickBooksDesktopRepository _repo;
        private readonly IQuickBooksRepository _online;
        private readonly IQuickBooksDesktopExportService _export;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly ITenantContext _tenantContext;

        public QuickBooksDesktopController(
            IQuickBooksDesktopRepository repo,
            IQuickBooksRepository online,
            IQuickBooksDesktopExportService export,
            Services.Audit.IAuditLogger audit,
            ITenantContext tenantContext)
        {
            _repo = repo;
            _online = online;
            _export = export;
            _audit = audit;
            _tenantContext = tenantContext;
        }

        private Guid TenantId => _tenantContext.TenantId;
        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        // ── Setup ────────────────────────────────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost("Setup")]
        public async Task<IActionResult> Setup([FromBody] QbdSetupRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (await _online.GetConnection(TenantId) is not null)
            {
                return new ApiResponses().BadRequestResult(
                    "This track is connected to QuickBooks Online. Disconnect it before switching to QuickBooks Desktop.");
            }
            if (await _repo.GetSetup(TenantId) is not null)
            {
                return new ApiResponses().BadRequestResult("QuickBooks Desktop is already set up for this track.");
            }

            var start = req.SyncStartDate ?? LocalToday();
            await _repo.CreateSetup(TenantId, start, UserId);
            await _audit.Log("quickbooks.desktop_setup", $"Switched to QuickBooks Desktop, exporting from {start:yyyy-MM-dd}",
                tenantId: TenantId, metadata: new { syncStartDate = start });
            return new ApiResponses().OkResult(new { syncStartDate = start });
        }

        /// <summary>
        /// Move the first day exports cover. Only before the first export: once a file exists, moving
        /// the start could re-include days that file already carries, or strand days between them.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPut("StartDate")]
        public async Task<IActionResult> SetStartDate([FromBody] QbdSetupRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (req.SyncStartDate is null) return new ApiResponses().BadRequestResult("Choose a start date.");
            if (await _repo.GetSetup(TenantId) is null)
                return new ApiResponses().BadRequestResult("QuickBooks Desktop isn't set up for this track.");
            if ((await _repo.ListBatches(TenantId, 1)).Count > 0)
            {
                return new ApiResponses().BadRequestResult(
                    "The start date is fixed once a file has been exported, so no day can end up in two files.");
            }

            await _repo.SetSyncStartDate(TenantId, req.SyncStartDate.Value);
            await _audit.Log("quickbooks.desktop_start_date", $"QuickBooks Desktop exports now start {req.SyncStartDate:yyyy-MM-dd}",
                tenantId: TenantId, metadata: new { syncStartDate = req.SyncStartDate });
            return new ApiResponses().OkResult(new { syncStartDate = req.SyncStartDate });
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpDelete("Setup")]
        public async Task<IActionResult> RemoveSetup()
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            await _repo.DeleteSetup(TenantId);
            await _audit.Log("quickbooks.desktop_removed", "Stopped using QuickBooks Desktop", tenantId: TenantId);
            return new ApiResponses().OkResult(new { removed = true });
        }

        // ── Lists ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Take the Chart of Accounts and/or Class List from a Desktop "Export Lists to IIF Files"
        /// file. Replaces only the lists the file carries. Says which currently-mapped names the new
        /// lists no longer contain, since those will now block the next export.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost("Lists")]
        [RequestSizeLimit(MaxListFileBytes + 64 * 1024)]
        public async Task<IActionResult> UploadLists(IFormFile? file, CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (await _repo.GetSetup(TenantId) is null)
                return new ApiResponses().BadRequestResult("Switch this track to QuickBooks Desktop first.");
            if (file is null || file.Length == 0) return new ApiResponses().BadRequestResult("Choose the .IIF file QuickBooks Desktop saved.");
            if (file.Length > MaxListFileBytes)
                return new ApiResponses().BadRequestResult("That file is larger than 5 MB, which is far bigger than a chart of accounts. Check you chose the right file.");

            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                bytes = ms.ToArray();
            }

            IifLists lists;
            try { lists = IifListParser.Parse(IifFormat.Decode(bytes)); }
            catch (IifParseException ex) { return new ApiResponses().BadRequestResult(ex.Message); }

            List<QbdListItem>? accounts = lists.HasAccounts
                ? lists.Accounts.Select(a => new QbdListItem
                {
                    ListKind = "account", FullName = a.FullName, AccountType = a.AccountType,
                    AccountNumber = a.AccountNumber, IsHidden = a.IsHidden,
                }).ToList()
                : null;
            List<QbdListItem>? classes = lists.HasClasses
                ? lists.Classes.Select(c => new QbdListItem { ListKind = "class", FullName = c.FullName, IsHidden = c.IsHidden }).ToList()
                : null;

            await _repo.ReplaceLists(TenantId, lists.Product, accounts, classes);

            var accountNames = (accounts ?? await _repo.ListItems(TenantId, "account")).Select(a => a.FullName).ToHashSet(StringComparer.Ordinal);
            var classNames = (classes ?? await _repo.ListItems(TenantId, "class")).Select(c => c.FullName).ToHashSet(StringComparer.Ordinal);
            var missing = (await _repo.ListAccountMappings(TenantId)).Values.Where(n => !accountNames.Contains(n))
                .Concat((await _repo.ListClassMappings(TenantId)).Values.Where(n => !classNames.Contains(n)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            await _audit.Log("quickbooks.desktop_lists", "Uploaded QuickBooks Desktop lists", tenantId: TenantId,
                metadata: new { accounts = accounts?.Count, classes = classes?.Count, lists.Product });

            return new ApiResponses().OkResult(new QbdListUploadResponse
            {
                AccountCount = accounts?.Count,
                ClassCount = classes?.Count,
                Product = lists.Product,
                MissingMappedNames = missing,
            });
        }

        // ── Export ───────────────────────────────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost("Export")]
        public async Task<IActionResult> Export(CancellationToken ct)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");

            QbdExportResult result;
            try { result = await _export.ExportAsync(TenantId, UserId, ct); }
            catch (QbdExportException ex) { return new ApiResponses().BadRequestResult(ex.Message); }

            if (result.BatchId is not null)
            {
                await _audit.Log("quickbooks.desktop_export",
                    $"Exported {result.DaysInFile} day(s) to QuickBooks Desktop ({result.FromDate:yyyy-MM-dd} to {result.ToDate:yyyy-MM-dd})",
                    targetKind: "qbd_export_batch", targetId: result.BatchId, tenantId: TenantId);
            }

            return new ApiResponses().OkResult(new QbdExportResponse
            {
                BatchId = result.BatchId,
                DaysInFile = result.DaysInFile,
                DaysWithNoActivity = result.DaysWithNoActivity,
                FromDate = result.FromDate,
                ToDate = result.ToDate,
                LastCompleteDay = result.LastCompleteDay,
                MoreRemaining = result.MoreRemaining,
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpGet("Batches")]
        public async Task<IActionResult> Batches([FromQuery] int take = 50)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _repo.ListBatches(TenantId, Math.Clamp(take, 1, 200));
            return new ApiResponses().OkResult(rows.Select(b => new QbdExportBatchResponse
            {
                Id = b.Id,
                FromDate = b.FromDate,
                ToDate = b.ToDate,
                DayCount = b.DayCount,
                TotalDebitsCents = b.TotalDebitsCents,
                DownloadCount = b.DownloadCount,
                LastDownloadedAtUtc = b.LastDownloadedAtUtc,
                VoidedAtUtc = b.VoidedAtUtc,
                CreatedAt = b.CreatedAt,
            }).ToList());
        }

        /// <summary>
        /// The file exactly as first built, every time. A void batch is refused: its days have been
        /// released and may already sit in a newer file, so importing this one would double them.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpGet("Batches/{id:guid}/File")]
        public async Task<IActionResult> Download(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var batch = await _repo.GetBatch(TenantId, id);
            if (batch is null) return new ApiResponses().NotFoundResult("Export not found.");
            if (batch.VoidedAtUtc is not null)
            {
                return new ApiResponses().BadRequestResult(
                    "This export was voided, so its days may be in a newer file. Download the newer one instead.");
            }

            await _repo.MarkDownloaded(TenantId, id);
            var slug = new string((_tenantContext.Tenant.Subdomain ?? "ridepass").Where(char.IsLetterOrDigit).ToArray());
            var filename = $"RidePass-{slug}-{batch.FromDate:yyyyMMdd}-{batch.ToDate:yyyyMMdd}.iif";
            return File(IifFormat.Encode(batch.FileContent), "application/octet-stream", filename);
        }

        /// <summary>
        /// For a file that was NOT imported (or was undone in QuickBooks): releases its days so the
        /// next export includes them again.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.AccountingManage)]
        [HttpPost("Batches/{id:guid}/Void")]
        public async Task<IActionResult> Void(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var batch = await _repo.GetBatch(TenantId, id);
            if (batch is null) return new ApiResponses().NotFoundResult("Export not found.");

            if (!await _repo.VoidBatch(TenantId, id, UserId))
            {
                return new ApiResponses().BadRequestResult("This export was already voided.");
            }

            await _audit.Log("quickbooks.desktop_void",
                $"Voided QuickBooks Desktop export {batch.FromDate:yyyy-MM-dd} to {batch.ToDate:yyyy-MM-dd}; its days will export again",
                targetKind: "qbd_export_batch", targetId: id, tenantId: TenantId);
            return new ApiResponses().OkResult(new { voided = true });
        }

        private DateOnly LocalToday()
        {
            var iana = _tenantContext.Tenant.Timezone;
            if (string.IsNullOrWhiteSpace(iana)) return DateOnly.FromDateTime(DateTime.UtcNow);
            try
            {
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(iana)));
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return DateOnly.FromDateTime(DateTime.UtcNow);
            }
        }
    }
}
