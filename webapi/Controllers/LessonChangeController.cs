using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Helpers;
using Services.Lessons;
using Services.Repositories.Data.LessonData;
using Services.Repositories.Interfaces;
using webapi.AuthPolicies;
using webapi.Controllers.API.Data.LessonChange;
using webapi.Helpers;
using webapi.Multitenancy;

namespace webapi.Controllers
{
    /// <summary>
    /// Lesson date changes (Script0319). A venue that sells lessons on rebook-only terms lets the rider
    /// move a booking to another date of the same lesson (same event type, same group name) up to its
    /// cutoff, never inside it or onto a same-day session. Staff can move any booking at any time.
    /// Cancelling a session for weather emails every rider a link to rebook (cutoff lifted) or take a
    /// refund (PurchaseController.WeatherRefund).
    ///
    /// Rider endpoints accept either the signed-in buyer or the ticket's redemption token from the
    /// emailed link, so a guest buyer can act on it too.
    /// </summary>
    [ApiController]
    [Route("api/LessonChange")]
    [Authorize]
    public class LessonChangeController : ControllerBase
    {
        private readonly ILessonChangeRepository _lessons;
        private readonly IEventTicketTierRepository _tiers;
        private readonly ITenantRepository _tenants;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ISmtpEmailer _emailer;
        private readonly Services.Helpers.Interfaces.IDbHelper _db;
        private readonly Services.Audit.IAuditLogger _audit;
        private readonly Services.Waitlist.IWaitlistPromoter _waitlistPromoter;
        private readonly IConfiguration _config;
        private readonly ILogger<LessonChangeController> _logger;
        private readonly ITenantContext _tenantContext;

        public LessonChangeController(ILessonChangeRepository lessons, IEventTicketTierRepository tiers,
            ITenantRepository tenants, IEmailSuppressionRepository suppression, ISmtpEmailer emailer,
            Services.Helpers.Interfaces.IDbHelper db, Services.Audit.IAuditLogger audit,
            Services.Waitlist.IWaitlistPromoter waitlistPromoter, IConfiguration config,
            ILogger<LessonChangeController> logger, ITenantContext tenantContext)
        {
            _lessons = lessons;
            _tiers = tiers;
            _tenants = tenants;
            _suppression = suppression;
            _emailer = emailer;
            _db = db;
            _audit = audit;
            _waitlistPromoter = waitlistPromoter;
            _config = config;
            _logger = logger;
            _tenantContext = tenantContext;
        }

        // ── Rider ────────────────────────────────────────────────────────────

        [AllowAnonymous]
        [HttpGet("Ticket/{id:guid}")]
        public async Task<IActionResult> GetForRider(Guid id, [FromQuery] Guid? token)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ctx = await RiderTicket(id, token);
            if (ctx is null) return new ApiResponses().NotFoundResult("Booking not found.");
            return new ApiResponses().OkResult(await BuildInfo(ctx, staff: false));
        }

        [AllowAnonymous]
        [HttpPost("Ticket/{id:guid}/Rebook")]
        public async Task<IActionResult> RebookForRider(Guid id, [FromBody] LessonRebookRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ctx = await RiderTicket(id, request.Token);
            if (ctx is null) return new ApiResponses().NotFoundResult("Booking not found.");
            return await Rebook(ctx, request.TierId, staff: false);
        }

        // ── Staff ────────────────────────────────────────────────────────────

        [Authorize(Policy = TenantPermissions.Policy.SalesView)]
        [HttpGet("Sessions")]
        public async Task<IActionResult> Sessions([FromQuery] DateTime? fromUtc, [FromQuery] int days = 21)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var from = fromUtc ?? DateTime.UtcNow.AddDays(-1);
            var rows = await _lessons.ListSessions(_tenantContext.TenantId, from, from.AddDays(Math.Clamp(days, 1, 120)));
            return new ApiResponses().OkResult(rows.Select(r => new LessonSessionItem
            {
                EventId = r.EventId,
                Title = r.Title,
                StartsAt = Utc(r.StartsAt),
                EndsAt = Utc(r.EndsAt),
                Status = r.Status,
                WeatherCancelledAt = r.WeatherCancelledAt is DateTime w ? Utc(w) : null,
                Booked = r.Booked,
            }).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesView)]
        [HttpGet("Sessions/{eventId:guid}/Bookings")]
        public async Task<IActionResult> Bookings(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _lessons.ListBookings(_tenantContext.TenantId, eventId);
            return new ApiResponses().OkResult(rows.Select(b => new LessonBookingItem
            {
                TicketId = b.TicketId,
                Status = b.Status,
                TierName = b.TierName,
                RiderName = b.RiderName,
                PurchaserName = b.PurchaserName,
                PurchaserEmail = b.PurchaserEmail,
                AmountCents = b.AmountCents,
            }).ToList());
        }

        /// <summary>Bikes rented for a session and the rider height/weight each was set up for.</summary>
        [Authorize(Policy = TenantPermissions.Policy.SalesView)]
        [HttpGet("Sessions/{eventId:guid}/Bikes")]
        public async Task<IActionResult> Bikes(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var rows = await _lessons.ListBikes(_tenantContext.TenantId, eventId);
            return new ApiResponses().OkResult(rows.Select(b => new LessonBikeItem
            {
                RentalId = b.RentalId,
                Status = b.Status,
                RenterName = b.RenterName,
                Items = b.Items,
                RiderHeightIn = b.RiderHeightIn,
                RiderWeightLb = b.RiderWeightLb,
            }).ToList());
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesCancel)]
        [HttpGet("Staff/Ticket/{id:guid}")]
        public async Task<IActionResult> GetForStaff(Guid id)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ctx = await _lessons.GetTicket(_tenantContext.TenantId, id);
            if (ctx is null) return new ApiResponses().NotFoundResult("Booking not found.");
            return new ApiResponses().OkResult(await BuildInfo(ctx, staff: true));
        }

        [Authorize(Policy = TenantPermissions.Policy.SalesCancel)]
        [HttpPost("Staff/Ticket/{id:guid}/Rebook")]
        public async Task<IActionResult> RebookForStaff(Guid id, [FromBody] LessonRebookRequest request)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            var ctx = await _lessons.GetTicket(_tenantContext.TenantId, id);
            if (ctx is null) return new ApiResponses().NotFoundResult("Booking not found.");
            return await Rebook(ctx, request.TierId, staff: true);
        }

        /// <summary>
        /// Cancels a lesson session for weather and emails every booked rider a link to rebook another
        /// date or take a refund. Nobody is refunded automatically: the rider chooses.
        /// </summary>
        [Authorize(Policy = TenantPermissions.Policy.CatalogManage)]
        [HttpPost("Sessions/{eventId:guid}/WeatherCancel")]
        public async Task<IActionResult> WeatherCancel(Guid eventId)
        {
            if (!_tenantContext.IsResolved) return new ApiResponses().BadRequestResult("No tenant resolved.");
            if (!Guid.TryParse(User.FindFirst("UserId")?.Value, out var staffId))
                return new ApiResponses().BadRequestResult("Invalid token.");
            var tenantId = _tenantContext.TenantId;

            var sessions = await _lessons.ListSessions(tenantId, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddYears(2));
            var session = sessions.FirstOrDefault(s => s.EventId == eventId);
            if (session is null) return new ApiResponses().NotFoundResult("Lesson session not found.");
            if (session.WeatherCancelledAt is not null)
                return new ApiResponses().BadRequestResult("This session was already cancelled for weather.");
            if (!await _lessons.WeatherCancel(tenantId, eventId, staffId))
                return new ApiResponses().BadRequestResult("This session couldn't be cancelled.");

            var bookings = (await _lessons.ListBookings(tenantId, eventId)).Where(b => b.Status == "paid").ToList();
            var emailed = 0;
            foreach (var b in bookings)
            {
                if (await SendWeatherEmail(b, session)) emailed++;
            }
            await _audit.Log("lesson.weather_cancel", $"Cancelled \"{session.Title}\" for weather; {bookings.Count} booking(s) offered a rebook or refund",
                "event", eventId, tenantId, new { bookings = bookings.Count, emailed });
            return new ApiResponses().OkResult(new WeatherCancelResponse { Bookings = bookings.Count, Emailed = emailed });
        }

        // ── Shared ───────────────────────────────────────────────────────────

        /// <summary>The ticket, when the caller is its signed-in buyer or holds its redemption token.</summary>
        private async Task<LessonTicketContext?> RiderTicket(Guid id, Guid? token)
        {
            var ctx = await _lessons.GetTicket(_tenantContext.TenantId, id);
            if (ctx is null) return null;
            if (token is Guid t && t != Guid.Empty && t == ctx.RedemptionToken) return ctx;
            if (Guid.TryParse(User.FindFirst("UserId")?.Value, out var userId) && ctx.PurchaserUserId == userId) return ctx;
            return null;
        }

        private async Task<LessonChangeInfoResponse> BuildInfo(LessonTicketContext ctx, bool staff)
        {
            var tenant = _tenantContext.Tenant;
            var now = DateTime.UtcNow;
            var weather = ctx.WeatherCancelledAt is not null;
            var hasRental = await _lessons.HasSessionRental(tenant.Id, ctx.EventId, ctx.PurchaserUserId, ctx.PurchaserEmail);
            var block = BlockReason(ctx, staff, hasRental, now);

            var info = new LessonChangeInfoResponse
            {
                TicketId = ctx.TicketId,
                Status = ctx.Status,
                EventTitle = ctx.EventTitle,
                TierName = ctx.TierName,
                RiderName = ctx.RiderName,
                SessionStartsAt = Utc(ctx.SessionStartsAt),
                AmountCents = ctx.AmountCents,
                CutoffHours = tenant.LessonChangeCutoffHours,
                WeatherCancelled = weather,
                HasRental = hasRental,
                BlockReason = block,
            };
            if (block is null)
                info.Options = (await Options(ctx, staff, now)).Select(o => new LessonRebookOptionItem
                {
                    EventId = o.EventId,
                    EventTitle = o.EventTitle,
                    TierId = o.TierId,
                    TierName = o.TierName,
                    StartsAt = Utc(o.StartsAt),
                    EndsAt = Utc(o.EndsAt),
                    SpotsLeft = o.SpotsLeft,
                }).ToList();
            info.CanRebook = block is null && info.Options.Count > 0;
            if (block is null && info.Options.Count == 0)
                info.BlockReason = "There are no other dates for this lesson with room right now.";

            if (!staff && weather && ctx.Status == "paid")
            {
                info.CanRefund = IsCardPaid(ctx.PaymentMethod) && ctx.AmountCents > 0;
                if (!info.CanRefund)
                    info.RefundNote = ctx.AmountCents == 0
                        ? "Nothing was paid for this booking, so there's nothing to refund."
                        : "This booking wasn't paid by card online, so the venue will refund you directly. Contact them.";
            }
            return info;
        }

        /// <summary>Why this booking can't be moved by this caller, or null.</summary>
        private string? BlockReason(LessonTicketContext ctx, bool staff, bool hasRental, DateTime now)
        {
            var tenant = _tenantContext.Tenant;
            if (!ctx.IsLesson) return "Only lesson bookings can be moved to another date here.";
            if (ctx.Status != "paid")
                return ctx.Status == "redeemed" ? "This lesson has already been checked in." : "This booking is no longer active.";
            if (staff) return null;
            var weather = ctx.WeatherCancelledAt is not null;
            if (!weather && !tenant.LessonRebookOnly) return "This venue doesn't offer online date changes. Contact them to change your lesson.";
            if (!weather && ctx.EventStatus != "scheduled") return "This session was cancelled. Contact the venue.";
            if (hasRental) return "Your booking includes a bike rental for this session, so the venue needs to move it for you. Contact them.";
            return LessonChangeRules.RiderBlockReason(now, DateTime.SpecifyKind(ctx.SessionStartsAt, DateTimeKind.Utc),
                tenant.LessonChangeCutoffHours, weather);
        }

        private async Task<List<LessonRebookOption>> Options(LessonTicketContext ctx, bool staff, DateTime now)
        {
            var tenant = _tenantContext.Tenant;
            var zone = TenantClock.Resolve(tenant.Timezone);
            var weather = ctx.WeatherCancelledAt is not null;
            var all = await _lessons.ListOptions(tenant.Id, ctx.EventTypeId, ctx.EventId, ctx.TierKind, now);
            return all.Where(o => LessonChangeRules.SameGroup(o.TierName, ctx.TierName)
                    && (staff || LessonChangeRules.IsAllowedTarget(now, DateTime.SpecifyKind(o.StartsAt, DateTimeKind.Utc),
                        zone, tenant.LessonChangeCutoffHours, weather)))
                .ToList();
        }

        private async Task<IActionResult> Rebook(LessonTicketContext ctx, Guid tierId, bool staff)
        {
            var tenantId = _tenantContext.TenantId;
            var now = DateTime.UtcNow;
            var hasRental = await _lessons.HasSessionRental(tenantId, ctx.EventId, ctx.PurchaserUserId, ctx.PurchaserEmail);
            if (BlockReason(ctx, staff, hasRental, now) is { } block) return new ApiResponses().BadRequestResult(block);

            var target = (await Options(ctx, staff, now)).FirstOrDefault(o => o.TierId == tierId);
            if (target is null) return new ApiResponses().BadRequestResult("That date isn't available for this lesson. Pick another.");

            Guid? byUserId = Guid.TryParse(User.FindFirst("UserId")?.Value, out var uid) ? uid : null;
            var source = staff ? "staff" : ctx.WeatherCancelledAt is not null ? "weather" : "rider";
            string? error;
            // Same lock checkout takes for the target event, so a rebook can't oversell its last spot.
            await using (await _db.AcquireAdvisoryLock($"event-capacity:{target.EventId}"))
            {
                error = await _lessons.MoveTicket(tenantId, ctx.TicketId, ctx.TierId, target.TierId, source, byUserId);
            }
            if (error is not null) return new ApiResponses().BadRequestResult(error);

            await _audit.Log("lesson.rebook",
                $"Moved a lesson booking from \"{ctx.EventTitle}\" to \"{target.EventTitle}\" ({source})",
                "event_ticket_purchase", ctx.TicketId, tenantId,
                new { fromTierId = ctx.TierId, toTierId = target.TierId, fromEventId = ctx.EventId, toEventId = target.EventId, source });

            // The old group has a spot again.
            var oldTier = await _tiers.GetById(ctx.TierId, tenantId);
            _ = _waitlistPromoter.PromoteNext(ctx.EventId, ctx.TierId, oldTier?.LadderGroup);
            await SendRebookedEmail(ctx, target);

            var moved = await _lessons.GetTicket(tenantId, ctx.TicketId);
            return new ApiResponses().OkResult(moved is null ? null : await BuildInfo(moved, staff));
        }

        private static bool IsCardPaid(string method) => method is "stripe" or "stripe_connect" or "stripe_direct";

        private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);

        private string LocalWhen(DateTime utc)
        {
            var zone = TenantClock.Resolve(_tenantContext.Tenant.Timezone);
            return TimeZoneInfo.ConvertTimeFromUtc(Utc(utc), zone).ToString("dddd, MMMM d 'at' h:mm tt");
        }

        private string Site()
        {
            var apex = _config["App:RootDomain"] ?? "ridepass.io";
            return $"https://{_tenantContext.Tenant.Subdomain}.{apex}";
        }

        private async Task<bool> SendWeatherEmail(LessonBookingRow b, LessonSessionRow session)
        {
            if (!_emailer.IsConfigured || string.IsNullOrWhiteSpace(b.PurchaserEmail)) return false;
            try
            {
                var tenant = _tenantContext.Tenant;
                if (await _suppression.IsSuppressed(b.PurchaserEmail, tenant.Id, marketing: false)) return false;
                var enc = (string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
                var link = $"{Site()}/Lesson/{b.TicketId}?t={b.RedemptionToken}&weather=1";
                var html = $@"<p>Hi {enc(b.PurchaserName)},</p>
<p>We're sorry: <strong>{enc(session.Title)}</strong> on {enc(LocalWhen(session.StartsAt))} has been cancelled because of the weather.</p>
<p>{enc(b.RiderName)}'s booking ({enc(b.TierName)}) is still yours. Choose what you'd like:</p>
<ul><li>Move it to another date of the same lesson, or</li><li>Take a refund.</li></ul>
<p><a href=""{link}"" style=""display:inline-block;padding:10px 18px;background:#2e7d32;color:#fff;text-decoration:none;border-radius:4px"">Rebook or refund</a></p>
<p style=""color:#888"">Or open this link: {enc(link)}</p>";
                return await _emailer.Send(b.PurchaserEmail, $"{session.Title} was cancelled for weather", html, null,
                    Services.Email.TenantEmailIdentity.For(tenant));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to email weather cancellation for ticket {TicketId}", b.TicketId);
                return false;
            }
        }

        private async Task SendRebookedEmail(LessonTicketContext ctx, LessonRebookOption target)
        {
            if (!_emailer.IsConfigured || string.IsNullOrWhiteSpace(ctx.PurchaserEmail)) return;
            try
            {
                var tenant = _tenantContext.Tenant;
                if (await _suppression.IsSuppressed(ctx.PurchaserEmail, tenant.Id, marketing: false)) return;
                var enc = (string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
                var who = string.IsNullOrWhiteSpace(ctx.RiderName) ? "Your" : $"{ctx.RiderName}'s";
                var html = $@"<p>Hi {enc(ctx.PurchaserName)},</p>
<p>{enc(who)} lesson booking has moved to <strong>{enc(target.EventTitle)}</strong>, {enc(target.TierName)}, on {enc(LocalWhen(target.StartsAt))}.</p>
<p>The ticket and QR code you already have still work. See it any time at <a href=""{Site()}"">{enc(Site())}</a>.</p>";
                await _emailer.Send(ctx.PurchaserEmail, $"Your lesson moved to {LocalWhen(target.StartsAt)}", html, null,
                    Services.Email.TenantEmailIdentity.For(tenant));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to email lesson rebook for ticket {TicketId}", ctx.TicketId);
            }
        }
    }
}
