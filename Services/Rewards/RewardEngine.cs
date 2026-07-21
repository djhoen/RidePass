using Microsoft.Extensions.Logging;
using Services.Helpers;
using Services.Helpers.Interfaces;
using Services.Repositories.Data.RewardData;
using Services.Repositories.Interfaces;

namespace Services.Rewards
{
    public class RewardEngine : IRewardEngine
    {
        private readonly IRewardRepository _rewards;
        private readonly ITenantRepository _tenants;
        private readonly ISmtpEmailer _emailer;
        private readonly IEmailSuppressionRepository _suppression;
        private readonly ITenantCreditRepository _credit;
        private readonly IDbHelper _db;
        private readonly ILogger<RewardEngine> _logger;

        public RewardEngine(
            IRewardRepository rewards,
            ITenantRepository tenants,
            ISmtpEmailer emailer,
            IEmailSuppressionRepository suppression,
            ITenantCreditRepository credit,
            IDbHelper db,
            ILogger<RewardEngine> logger)
        {
            _rewards = rewards;
            _tenants = tenants;
            _emailer = emailer;
            _suppression = suppression;
            _credit = credit;
            _db = db;
            _logger = logger;
        }

        public async Task ProcessPaidPurchase(Guid tenantId, Guid userId, string riderEmail, string riderFirstName)
        {
            var programs = await _rewards.ListProgramsForTenant(tenantId, activeOnly: true);
            if (programs.Count == 0) return;

            // Auto-enroll into every "auto" program. Idempotent.
            foreach (var p in programs.Where(p => p.EnrollmentMode == "auto"))
            {
                await _rewards.CreateEnrollment(p.Id, userId);
            }

            var enrollments = await _rewards.ListEnrollmentsForUser(userId);
            var enrollmentByProgram = enrollments.ToDictionary(e => e.ProgramId);

            foreach (var program in programs)
            {
                if (!enrollmentByProgram.TryGetValue(program.Id, out var enrollment)) continue;

                // Serialize the count + mint per (program, rider) so two purchases finalizing at the
                // same moment can't both cross the threshold and mint two vouchers for one cycle. The
                // second waiter re-reads the now-incremented earned count and sees progress reset.
                await using var mintLock = await _db.AcquireAdvisoryLock($"reward-mint:{program.Id}:{userId}");

                // Auto programs enroll the rider on their very first qualifying purchase, so that
                // triggering purchase must count. Its created_at (checkout start) precedes the
                // enrollment's enrolled_at (stamped at finalize), so counting from enrolled_at would
                // drop it (off-by-one). Count auto programs from when the program began instead, which
                // still excludes pre-program purchases; opt-in programs count only purchases the rider
                // made after choosing to enroll.
                var countFromUtc = program.EnrollmentMode == "auto" ? program.CreatedAt : enrollment.EnrolledAt;
                var qualifyingCount = await _rewards.CountQualifyingPurchases(
                    tenantId, userId, program.RequirementKind, countFromUtc);

                // Subtract previously-earned redemptions so progress resets after each reward.
                var earned = (await _rewards.ListRedemptionsForProgram(program.Id))
                    .Count(r => r.UserId == userId);
                var progressTowardNext = qualifyingCount - (earned * program.RequirementCount);

                if (progressTowardNext >= program.RequirementCount)
                {
                    await _rewards.CreateRedemption(program.Id, userId);
                    await SendRewardEmail(tenantId, riderEmail, riderFirstName, program);
                    continue;
                }

                if (program.ProximityEmailThreshold is int threshold)
                {
                    var remaining = program.RequirementCount - progressTowardNext;
                    if (remaining == threshold && enrollment.LastProximityEmailedAtCount != progressTowardNext)
                    {
                        await SendProximityEmail(tenantId, riderEmail, riderFirstName, program, remaining);
                        await _rewards.UpdateLastProximityEmailedAtCount(enrollment.Id, progressTowardNext);
                    }
                }
            }
        }

        /// <summary>
        /// Credit-back loyalty (Script0196): pay a rate of the money collected back as store
        /// credit. Called from every settle point; sourceId keys idempotency (one award per
        /// settled purchase via the once-per-reference unique index, double-checked here so a
        /// webhook + reconciler double-fire doesn't email twice). Auto programs pay every
        /// customer (walk-ins by email/phone included); opt-in programs pay enrolled users.
        /// Best-effort by contract: callers wrap in try/catch, the sale never depends on this.
        /// </summary>
        public async Task AwardCreditBack(Guid tenantId, Guid? userId, string? email, string? name,
            string sourceKind, Guid sourceId, int spentCents)
        {
            if (spentCents <= 0) return;
            if (userId is null && string.IsNullOrWhiteSpace(email)) return;

            var programs = (await _rewards.ListProgramsForTenant(tenantId, activeOnly: true))
                .Where(p => p.RewardKind == "credit_rate" && p.CreditRateBps is > 0
                    && (p.CreditQualifyingKind == "any" || p.CreditQualifyingKind == sourceKind))
                .ToList();
            if (programs.Count == 0) return;

            // One award per settled purchase, race-guarded so the winner alone emails.
            await using var awardLock = await _db.AcquireAdvisoryLock($"credit-award:{sourceId}");
            if (await _credit.HasEntry(tenantId, "loyalty_award", sourceKind, sourceId)) return;

            var enrolled = userId.HasValue
                ? (await _rewards.ListEnrollmentsForUser(userId.Value)).Select(e => e.ProgramId).ToHashSet()
                : new HashSet<Guid>();
            var eligible = new List<RewardProgram>();
            foreach (var p in programs)
            {
                if (p.EnrollmentMode == "auto")
                {
                    if (userId.HasValue) await _rewards.CreateEnrollment(p.Id, userId.Value);
                    eligible.Add(p);
                }
                else if (userId.HasValue && enrolled.Contains(p.Id))
                {
                    eligible.Add(p);
                }
            }
            if (eligible.Count == 0) return;

            var award = eligible.Sum(p => (int)((long)spentCents * p.CreditRateBps!.Value / 10_000L));
            if (award <= 0) return;

            var account = await _credit.GetOrCreateAccount(tenantId, userId, email, null, name);
            if (account is null) return;
            var note = string.Join(" + ", eligible.Select(p => p.Name));
            if (!await _credit.TryAdjust(account.Id, tenantId, award, "loyalty_award",
                    sourceKind, sourceId, note, null))
                return;

            _logger.LogInformation("Loyalty credit-back: {Award}c to account {Account} for {Kind} {Id} (tenant {Tenant})",
                award, account.Id, sourceKind, sourceId, tenantId);

            // Tell them only when it's worth an email; small awards surface on their Rewards page.
            if (award >= 100 && !string.IsNullOrWhiteSpace(email))
                await SendCreditBackEmail(tenantId, email!, name?.Split(' ').FirstOrDefault() ?? "rider", award, note);
        }

        private async Task SendCreditBackEmail(Guid tenantId, string toEmail, string firstName, int awardCents, string programNames)
        {
            if (!_emailer.IsConfigured) return;
            if (await _suppression.IsSuppressed(toEmail, tenantId, marketing: false)) return;
            var amount = "$" + (awardCents / 100m).ToString("0.00");
            var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
<p>Your purchase just earned you <strong>{amount} in store credit</strong> from <strong>{System.Net.WebUtility.HtmlEncode(programNames)}</strong>.</p>
<p>It's on your account now: spend it at the counter or apply it at checkout next time you buy online.</p>";
            var tenant = await _tenants.GetById(tenantId);
            await _emailer.Send(toEmail, $"You earned {amount} in store credit", html, null,
                Services.Email.TenantEmailIdentity.For(tenant));
        }

        private async Task SendRewardEmail(Guid tenantId, string toEmail, string firstName, RewardProgram program)
        {
            if (!_emailer.IsConfigured) return;
            // Reward-earned is transactional; skip only hard-bounced addresses, not marketing opt-outs.
            if (await _suppression.IsSuppressed(toEmail, tenantId, marketing: false)) return;
            var rewardLine = program.RewardPercentOff == 100
                ? "a free pass / ticket"
                : $"{program.RewardPercentOff}% off your next {KindLabel(program.RequirementKind)}";
            var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
<p>You just earned a reward from <strong>{System.Net.WebUtility.HtmlEncode(program.Name)}</strong>: <strong>{rewardLine}</strong>.</p>
<p>It's waiting on your account — show it at the gate or apply it on your next purchase.</p>";
            var tenant = await _tenants.GetById(tenantId);
            await _emailer.Send(toEmail, $"You earned a reward — {program.Name}", html, null, Services.Email.TenantEmailIdentity.For(tenant));
        }

        private async Task SendProximityEmail(Guid tenantId, string toEmail, string firstName, RewardProgram program, int remaining)
        {
            if (!_emailer.IsConfigured) return;
            // Proximity nudges are marketing: honor unsubscribes and complaints (and hard bounces).
            // The reward-EARNED email stays transactional and is not gated here.
            if (await _suppression.IsSuppressed(toEmail, tenantId, marketing: true)) return;
            var rewardLine = program.RewardPercentOff == 100
                ? "a free pass / ticket"
                : $"{program.RewardPercentOff}% off";
            var noun = KindLabel(program.RequirementKind);
            var nounPlural = remaining == 1 ? noun : noun + "s";
            var html = $@"<p>Hi {System.Net.WebUtility.HtmlEncode(firstName)},</p>
<p>You're <strong>{remaining} {nounPlural} away</strong> from earning {rewardLine} in <strong>{System.Net.WebUtility.HtmlEncode(program.Name)}</strong>.</p>
<p>See you at the track soon!</p>";
            var tenant = await _tenants.GetById(tenantId);
            await _emailer.Send(toEmail, $"You're {remaining} away from a reward!", html, null, Services.Email.TenantEmailIdentity.For(tenant));
        }

        private static string KindLabel(string kind) => kind switch
        {
            "pass" => "pass",
            "event_ticket" => "event ticket",
            _ => "purchase",
        };
    }
}
