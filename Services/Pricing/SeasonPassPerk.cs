using Services.Repositories.Data.PaymentData;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;

namespace Services.Pricing
{
    /// <summary>What a pass holder gets off one sale, and where it came from.</summary>
    public readonly record struct SeasonPassPerk(int DiscountCents, string Label, bool IsPerPass)
    {
        public static readonly SeasonPassPerk None = new(0, "", false);
        public bool Any => DiscountCents > 0;

        /// <summary>
        /// One line naming every discount source that actually contributed, for the sale's
        /// discount_label snapshot. Takes the post-stacking amounts, not the resolved ones: when
        /// stacking is off exactly one survives, and labelling the loser would describe a discount
        /// the customer never got.
        ///
        /// Matters because a pass perk is applied without any staff action, so a $600 line on a
        /// sale would otherwise have no explanation on it at all.
        /// </summary>
        public static string? LabelFor(
            SeasonPassPerk perk, int perkCentsApplied, string? staffDiscountName, int staffCentsApplied)
        {
            var parts = new List<string>(2);
            if (perkCentsApplied > 0 && !string.IsNullOrEmpty(perk.Label)) parts.Add(perk.Label);
            if (staffCentsApplied > 0 && !string.IsNullOrWhiteSpace(staffDiscountName)) parts.Add(staffDiscountName!);
            return parts.Count == 0 ? null : string.Join(" + ", parts);
        }
    }

    /// <summary>
    /// The single place that decides a pass holder's discount on a sale, so the five tills that ask
    /// (F&amp;B, shop counter, shop online, rental counter, rental online) cannot drift on the answer.
    ///
    /// PRECEDENCE: a per-pass benefit beats the tenant-wide holder discount, and does not need the
    /// tenant-wide switch to be on. The reasoning is that they are different KINDS of thing. A
    /// per-pass benefit is product configuration ("the Employee Pass includes half-price food"),
    /// so it must keep working whatever a tenant does with their loyalty settings. The tenant-wide
    /// discount is the loyalty scheme itself ("any pass holder gets 10% off"), which is exactly
    /// what that switch governs. They are not summed: the holder gets the better arrangement, not
    /// both, or a $6,000 bike bought by an employee could go out at 60% off by accident.
    /// </summary>
    public interface ISeasonPassPerkResolver
    {
        /// <param name="benefitType">'concession', 'retail' or 'rental'.</param>
        /// <param name="onDateUtc">
        /// The date the perk must be valid on. For a rental this is the START date, not today, so a
        /// pass expiring before the rental begins does not discount it.
        /// </param>
        Task<SeasonPassPerk> Resolve(
            Guid? userId, Tenant tenant, string benefitType, int baseCents, DateTime onDateUtc);
    }

    public class SeasonPassPerkResolver : ISeasonPassPerkResolver
    {
        private readonly ISeasonPassRepository _passes;

        public SeasonPassPerkResolver(ISeasonPassRepository passes) => _passes = passes;

        public async Task<SeasonPassPerk> Resolve(
            Guid? userId, Tenant tenant, string benefitType, int baseCents, DateTime onDateUtc)
        {
            // A walk-in with no account carries no perk: there is no pass to read one off.
            if (userId is not Guid uid || baseCents <= 0) return SeasonPassPerk.None;

            var grants = await _passes.ListActiveBenefitGrantsForUser(
                uid, tenant.Id, benefitType, scopeId: null, onDateUtc: onDateUtc);

            // The tenant-wide fallback needs a second query, so only ask when it could matter:
            // a per-pass perk wins outright, and a surface that isn't switched on can't apply.
            var needsHolderCheck = grants.Count == 0 && tenant.SeasonPassDiscountAppliesTo(benefitType);
            var holdsPass = needsHolderCheck && await _passes.HasPassValidOn(uid, tenant.Id, onDateUtc);

            return Decide(grants, tenant, benefitType, baseCents, holdsPass);
        }

        /// <summary>
        /// The precedence rule on its own, with the I/O already done. Pure so the branch that
        /// decides how much money comes off a sale can be tested directly.
        /// </summary>
        public static SeasonPassPerk Decide(
            IReadOnlyList<SeasonPassBenefitGrant> grants, Tenant tenant, string benefitType,
            int baseCents, bool holdsActivePass)
        {
            if (baseCents <= 0) return SeasonPassPerk.None;

            if (grants.Count > 0)
            {
                // Best against THIS sale, not a nominal one: a $5-off perk beats 10% on a $30 sale
                // and loses on a $300 one, and the holder should get whichever is actually better.
                var best = grants.OrderByDescending(g => g.Benefit.DiscountFor(baseCents)).First();
                var cents = best.Benefit.DiscountFor(baseCents);
                if (cents > 0)
                {
                    // Named after the pass so the receipt and the discount report say which perk
                    // was given, rather than a generic "pass discount" nobody can trace.
                    return new SeasonPassPerk(cents, $"{best.ProductName} discount", IsPerPass: true);
                }
            }

            if (!tenant.SeasonPassDiscountAppliesTo(benefitType) || !holdsActivePass) return SeasonPassPerk.None;

            var tenantCents = ComputeDiscountCents(
                tenant.SeasonPassDiscountKind, tenant.SeasonPassDiscountValue, baseCents);
            return tenantCents > 0
                ? new SeasonPassPerk(tenantCents, "Season Pass discount", IsPerPass: false)
                : SeasonPassPerk.None;
        }

        /// <summary>
        /// 'percent' is basis points, 'amount' is cents. Never exceeds the base, so a $20-off perk
        /// on a $5 sale takes it to zero rather than owing the customer money. Half-up rounding, to
        /// match ComputeDiscountCents on the F&amp;B till.
        /// </summary>
        public static int ComputeDiscountCents(string kind, int value, int baseCents)
        {
            if (baseCents <= 0 || value <= 0) return 0;
            var cents = kind == "amount"
                ? value
                : (int)Math.Round(baseCents * (Math.Clamp(value, 0, 10000) / 10000.0), MidpointRounding.AwayFromZero);
            return Math.Clamp(cents, 0, baseCents);
        }
    }
}
