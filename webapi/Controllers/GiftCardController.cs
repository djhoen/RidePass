using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Repositories.Data.GiftCardData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.GiftCard;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    // Admin surface for gift cards: browse/search, single-card detail, legacy-balance CSV import
    // (for tenants migrating from another system, e.g. physical Card Dog cards), and void.
    // The BUY flow stays in PurchaseController; redemption stays in the checkout/register paths.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class GiftCardController : ControllerBase
    {
        // Keep any single import bounded: a stray export with the full transaction log in it
        // should fail loudly, not insert a million rows.
        private const int MaxImportRows = 5000;
        // Sanity cap per card. Deliberately NOT the tenant's gift_card_max_cents (that caps what a
        // buyer may PURCHASE); a legacy balance can legitimately exceed it.
        private const int MaxImportBalanceCents = 1_000_000;   // $10,000

        private readonly IGiftCardRepository _giftCards;
        private readonly ITenantContext _tenantContext;

        public GiftCardController(IGiftCardRepository giftCards, ITenantContext tenantContext)
        {
            _giftCards = giftCards;
            _tenantContext = tenantContext;
        }

        private Guid? UserId => Guid.TryParse(User.FindFirst("UserId")?.Value, out var id) ? id : null;

        [Authorize(Policy = TenantPermissions.Policy.SalesView)]
        [HttpGet("Admin/List")]
        public async Task<IActionResult> ListForAdmin(
            [FromQuery] string? search, [FromQuery] string? status,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 25)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var (items, total) = await _giftCards.ListForAdmin(
                _tenantContext.TenantId, Blank(search), Blank(status), page, pageSize);
            return new ApiResponses().OkResult(new GiftCardAdminListResponse
            {
                Total = total,
                Items = items.Select(c => new GiftCardAdminListResponse.Row
                {
                    Id = c.Id,
                    CodeMasked = MaskCode(c.Code),
                    InitialAmountCents = c.InitialAmountCents,
                    BalanceCents = c.BalanceCents,
                    Status = c.Status,
                    RecipientName = c.RecipientName,
                    RecipientEmail = c.RecipientEmail,
                    BuyerName = c.BuyerName,
                    Imported = c.ImportedFrom != null,
                    ImportedFrom = c.ImportedFrom,
                    CreatedAt = c.CreatedAt,
                }).ToList(),
            });
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesView)]
        [HttpGet("Admin/{id:guid}")]
        public async Task<IActionResult> GetForAdmin(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var c = await _giftCards.GetById(id, _tenantContext.TenantId);
            if (c is null) return new ApiResponses().NotFoundResult("Gift card not found.");
            var redemptions = await _giftCards.ListRedemptionsByCard(c.Id);
            return new ApiResponses().OkResult(new GiftCardAdminDetailResponse
            {
                Id = c.Id,
                Code = c.Code,
                InitialAmountCents = c.InitialAmountCents,
                BalanceCents = c.BalanceCents,
                Status = c.Status,
                DeliveryStatus = c.DeliveryStatus,
                BuyerName = c.BuyerName,
                BuyerEmail = c.BuyerEmail,
                RecipientName = c.RecipientName,
                RecipientEmail = c.RecipientEmail,
                PersonalNote = c.PersonalNote,
                Imported = c.ImportedFrom != null,
                ImportedFrom = c.ImportedFrom,
                ImportedAt = c.ImportedAt,
                CreatedAt = c.CreatedAt,
                Redemptions = redemptions.Select(r => new GiftCardAdminDetailResponse.RedemptionRow
                {
                    SourceKind = r.SourceKind,
                    AmountCents = r.AmountCents,
                    RedeemedAt = r.RedeemedAt,
                }).ToList(),
            });
        }

        // CSV import of legacy balances. Dry run validates and reports; commit inserts row by row
        // (ON CONFLICT DO NOTHING makes an already-existing code a per-row skip, so re-running an
        // import file is safe and reports the overlap instead of duplicating cards).
        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Admin/Import")]
        public async Task<IActionResult> Import([FromBody] GiftCardImportRequest req)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var source = Blank(req.Source) ?? "Legacy import";

            var report = new GiftCardImportReportResponse { DryRun = req.DryRun };
            var rows = ParseCsv(req.CsvText, report.Errors);
            report.TotalRows = rows.Count + report.Errors.Count;
            if (report.TotalRows > MaxImportRows)
                return new ApiResponses().BadRequestResult(
                    $"That file has {report.TotalRows} rows; the limit per import is {MaxImportRows}. Split it and try again.");

            // Duplicates inside the file are always an error (which balance would win?).
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows.ToList())
            {
                if (!seen.Add(row.Code))
                {
                    report.Errors.Add(new GiftCardImportReportResponse.RowError
                    { Line = row.Line, Code = row.Code, Reason = "Duplicate code within the file." });
                    rows.Remove(row);
                }
            }

            foreach (var row in rows)
            {
                if (req.DryRun)
                {
                    // Report collisions with existing cards without writing anything.
                    var existing = await _giftCards.GetByCode(_tenantContext.TenantId, row.Code);
                    if (existing != null)
                    {
                        report.Errors.Add(new GiftCardImportReportResponse.RowError
                        { Line = row.Line, Code = row.Code, Reason = "A gift card with this code already exists." });
                        continue;
                    }
                }
                else
                {
                    var id = await _giftCards.ImportCard(new GiftCard
                    {
                        TenantId = _tenantContext.TenantId,
                        Code = row.Code,
                        InitialAmountCents = row.BalanceCents,
                        BalanceCents = row.BalanceCents,
                        RecipientName = row.RecipientName,
                        RecipientEmail = row.RecipientEmail,
                        ImportedFrom = source,
                        ImportedByUserId = UserId,
                    });
                    if (id is null)
                    {
                        report.Errors.Add(new GiftCardImportReportResponse.RowError
                        { Line = row.Line, Code = row.Code, Reason = "A gift card with this code already exists." });
                        continue;
                    }
                }
                report.Imported++;
                report.TotalBalanceCents += row.BalanceCents;
            }

            return new ApiResponses().OkResult(report);
        }

        [Authorize(Policy = TenantPermissions.Policy.SettingsManage)]
        [HttpPost("Admin/{id:guid}/Void")]
        public async Task<IActionResult> VoidCard(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var c = await _giftCards.GetById(id, _tenantContext.TenantId);
            if (c is null) return new ApiResponses().NotFoundResult("Gift card not found.");
            if (!await _giftCards.VoidActive(id, _tenantContext.TenantId))
                return new ApiResponses().BadRequestResult($"Only active cards can be voided; this one is '{c.Status}'.");
            return new ApiResponses().OkResult();
        }

        // ── CSV parsing ─────────────────────────────────────────────────────────
        private sealed class ImportRow
        {
            public int Line;
            public string Code = null!;
            public int BalanceCents;
            public string? RecipientName;
            public string? RecipientEmail;
        }

        // code,balance[,recipient_name[,recipient_email]] — balance in dollars ("25" or "25.00",
        // "$" and thousands separators tolerated). A header line is detected and skipped. Kept
        // deliberately simple: legacy exports are plain comma files; quoted commas inside a name
        // are the only quoting handled.
        private static List<ImportRow> ParseCsv(string csvText, List<GiftCardImportReportResponse.RowError> errors)
        {
            var rows = new List<ImportRow>();
            var lines = csvText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var raw = lines[i].Trim();
                if (raw.Length == 0) continue;
                var cells = SplitCsvLine(raw);
                var code = cells.ElementAtOrDefault(0)?.Trim();
                var balanceText = cells.ElementAtOrDefault(1)?.Trim();

                // Header detection: only the first non-empty line, and only when it actually
                // looks like a header ("code"/"balance"-style words) — a malformed first DATA row
                // must produce an error, not vanish.
                if (rows.Count == 0 && errors.Count == 0
                    && i == Array.FindIndex(lines, l => l.Trim().Length > 0)
                    && !TryParseDollars(balanceText, out _)
                    && (string.Equals(code, "code", StringComparison.OrdinalIgnoreCase)
                        || (balanceText != null && System.Text.RegularExpressions.Regex.IsMatch(balanceText, "balance|amount|value", System.Text.RegularExpressions.RegexOptions.IgnoreCase))))
                    continue;

                if (string.IsNullOrWhiteSpace(code))
                {
                    errors.Add(new GiftCardImportReportResponse.RowError { Line = i + 1, Reason = "Missing code." });
                    continue;
                }
                if (code.Length > 64)
                {
                    errors.Add(new GiftCardImportReportResponse.RowError { Line = i + 1, Code = code, Reason = "Code is longer than 64 characters." });
                    continue;
                }
                if (!TryParseDollars(balanceText, out var cents))
                {
                    errors.Add(new GiftCardImportReportResponse.RowError { Line = i + 1, Code = code, Reason = $"'{balanceText}' isn't a valid dollar amount." });
                    continue;
                }
                if (cents <= 0)
                {
                    errors.Add(new GiftCardImportReportResponse.RowError { Line = i + 1, Code = code, Reason = "Balance must be greater than zero (skip fully-spent cards)." });
                    continue;
                }
                if (cents > MaxImportBalanceCents)
                {
                    errors.Add(new GiftCardImportReportResponse.RowError { Line = i + 1, Code = code, Reason = $"Balance exceeds the ${MaxImportBalanceCents / 100:N0} per-card sanity cap." });
                    continue;
                }
                rows.Add(new ImportRow
                {
                    Line = i + 1,
                    Code = code,
                    BalanceCents = cents,
                    RecipientName = Blank(cells.ElementAtOrDefault(2)),
                    RecipientEmail = Blank(cells.ElementAtOrDefault(3)),
                });
            }
            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var current = new System.Text.StringBuilder();
            var inQuotes = false;
            foreach (var ch in line)
            {
                if (ch == '"') { inQuotes = !inQuotes; continue; }
                if (ch == ',' && !inQuotes) { cells.Add(current.ToString()); current.Clear(); continue; }
                current.Append(ch);
            }
            cells.Add(current.ToString());
            return cells;
        }

        private static bool TryParseDollars(string? text, out int cents)
        {
            cents = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var cleaned = text.Replace("$", "").Replace(",", "").Trim();
            if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var dollars)) return false;
            cents = (int)Math.Round(dollars * 100, MidpointRounding.AwayFromZero);
            return true;
        }

        private static string MaskCode(string code) =>
            code.Length <= 4 ? code : $"…{code[^4..]}";

        private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
