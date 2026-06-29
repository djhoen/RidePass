using Services.Repositories.Data.TenantData;

namespace Services.Payments
{
    /// <summary>
    /// How a charge should be created for a tenant. For 'platform' tenants both fields are null
    /// (charge on the platform account, internal split, monthly payout). For 'direct' tenants the
    /// charge runs on <see cref="ConnectedAccountId"/> with <see cref="ApplicationFeeCents"/> routed
    /// to the platform as RidePass's service fee.
    /// </summary>
    public record ChargePlan(string? ConnectedAccountId, long? ApplicationFeeCents)
    {
        public bool IsDirect => !string.IsNullOrEmpty(ConnectedAccountId);
        public static readonly ChargePlan Platform = new(null, null);
    }

    public interface IChargeRouter
    {
        /// <summary>
        /// Decides whether <paramref name="tenant"/>'s charge runs on the platform account or as a
        /// direct charge on their own connected account. <paramref name="serviceFeeCents"/> is the
        /// total RidePass service fee for this charge; in direct mode it becomes the Stripe
        /// application fee (that is how RidePass gets paid). <paramref name="chargeAmountCents"/> is
        /// the amount actually being charged on the card; the application fee is clamped to it so a
        /// gift-card-reduced charge can never have an app fee larger than the charge (Stripe rejects
        /// that). Throws if the tenant is set to 'direct' but has no connected account, so we never
        /// silently route a direct-mode tenant's funds through the platform's aggregate merchant
        /// account (the card-network compliance trap).
        /// </summary>
        ChargePlan Plan(Tenant tenant, long serviceFeeCents, long chargeAmountCents);
    }

    public class ChargeRouter : IChargeRouter
    {
        public ChargePlan Plan(Tenant tenant, long serviceFeeCents, long chargeAmountCents)
        {
            if (tenant.StripeChargeMode != "direct")
            {
                return ChargePlan.Platform;
            }
            if (string.IsNullOrEmpty(tenant.StripeConnectAccountId))
            {
                throw new InvalidOperationException(
                    "This track is set to charge on its own Stripe account but no connected account is linked yet. Connect the account in Settings before taking payments.");
            }
            var appFee = serviceFeeCents < 0 ? 0 : serviceFeeCents;
            if (appFee > chargeAmountCents) appFee = chargeAmountCents < 0 ? 0 : chargeAmountCents;
            return new ChargePlan(tenant.StripeConnectAccountId, appFee);
        }
    }
}
