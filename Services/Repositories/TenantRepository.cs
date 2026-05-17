using Services.Helpers.Interfaces;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantRepository : ITenantRepository
    {
        private const string SelectColumns = @"
            id, subdomain, display_name AS DisplayName, status,
            tenant_type AS TenantType, timezone,
            require_reservation_for_passes AS RequireReservationForPasses,
            require_emergency_contact AS RequireEmergencyContact,
            allow_event_subscriptions AS AllowEventSubscriptions,
            stripe_connect_account_id AS StripeConnectAccountId,
            stripe_connect_status AS StripeConnectStatus,
            stripe_terminal_location_id AS StripeTerminalLocationId,
            service_charge_bps AS ServiceChargeBps,
            monthly_service_charge_cap_cents AS MonthlyServiceChargeCapCents,
            shipping_name AS ShippingName,
            about_html AS AboutHtml,
            hours_json::text AS HoursJson,
            home_next_up_title AS HomeNextUpTitle,
            home_next_up_event_type_ids AS HomeNextUpEventTypeIds,
            daily_status_open AS DailyStatusOpen,
            daily_status_message AS DailyStatusMessage,
            daily_status_updated_at AS DailyStatusUpdatedAt,
            contact_email AS ContactEmail,
            phone AS Phone,
            social_facebook_url AS SocialFacebookUrl,
            social_instagram_url AS SocialInstagramUrl,
            social_tiktok_url AS SocialTiktokUrl,
            social_youtube_url AS SocialYoutubeUrl,
            refund_policy_html AS RefundPolicyHtml,
            address_line AS AddressLine, city, region, postal_code AS PostalCode, country,
            latitude, longitude,
            gift_cards_enabled AS GiftCardsEnabled,
            gift_card_min_cents AS GiftCardMinCents,
            gift_card_max_cents AS GiftCardMaxCents,
            rentals_enabled AS RentalsEnabled,
            extras_enabled AS ExtrasEnabled,
            season_passes_enabled AS SeasonPassesEnabled,
            allow_self_cancel AS AllowSelfCancel,
            waitlist_enabled AS WaitlistEnabled,
            waitlist_confirm_window_minutes AS WaitlistConfirmWindowMinutes,
            membership_enabled AS MembershipEnabled,
            membership_name AS MembershipName,
            membership_price_cents AS MembershipPriceCents,
            membership_duration_kind AS MembershipDurationKind,
            membership_required_for_riders AS MembershipRequiredForRiders,
            membership_required_for_spectators AS MembershipRequiredForSpectators,
            created_at AS CreatedAt, updated_at AS UpdatedAt";

        private readonly IDbHelper _db;

        public TenantRepository(IDbHelper db)
        {
            _db = db;
        }

        public async Task<Tenant?> GetBySubdomain(string subdomain)
        {
            var sql = $"SELECT {SelectColumns} FROM tenant WHERE subdomain = @subdomain LIMIT 1";
            var result = await _db.Query<Tenant>(sql, new { subdomain });
            return result.FirstOrDefault();
        }

        public async Task<Tenant?> GetById(Guid id)
        {
            var sql = $"SELECT {SelectColumns} FROM tenant WHERE id = @id LIMIT 1";
            var result = await _db.Query<Tenant>(sql, new { id });
            return result.FirstOrDefault();
        }

        public async Task<Guid> Create(Tenant tenant)
        {
            const string sql = @"
                INSERT INTO tenant (subdomain, display_name, status, tenant_type, timezone)
                VALUES (@Subdomain, @DisplayName, @Status, @TenantType, @Timezone)
                RETURNING id";
            var result = await _db.Query<Guid>(sql, tenant);
            return result.First();
        }

        public async Task UpdateTimezone(Guid tenantId, string timezone)
        {
            const string sql = "UPDATE tenant SET timezone = @timezone WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, timezone });
        }

        public async Task UpdateRequireReservation(Guid tenantId, bool require)
        {
            const string sql = "UPDATE tenant SET require_reservation_for_passes = @require WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, require });
        }

        public async Task UpdateRequireEmergencyContact(Guid tenantId, bool require)
        {
            const string sql = "UPDATE tenant SET require_emergency_contact = @require WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, require });
        }

        public async Task UpdateAllowEventSubscriptions(Guid tenantId, bool allow)
        {
            const string sql = "UPDATE tenant SET allow_event_subscriptions = @allow WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, allow });
        }

        public async Task SetStripeConnectAccount(Guid tenantId, string accountId, string status)
        {
            const string sql = @"
                UPDATE tenant
                SET stripe_connect_account_id = @accountId,
                    stripe_connect_status = @status
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, accountId, status });
        }

        public async Task UpdateStripeConnectStatus(string accountId, string status)
        {
            const string sql = @"
                UPDATE tenant
                SET stripe_connect_status = @status
                WHERE stripe_connect_account_id = @accountId";
            await _db.Execute(sql, new { accountId, status });
        }

        public async Task<Tenant?> GetByStripeConnectAccountId(string accountId)
        {
            var sql = $"SELECT {SelectColumns} FROM tenant WHERE stripe_connect_account_id = @accountId LIMIT 1";
            var result = await _db.Query<Tenant>(sql, new { accountId });
            return result.FirstOrDefault();
        }

        public async Task ClearStripeConnect(Guid tenantId)
        {
            const string sql = @"
                UPDATE tenant
                SET stripe_connect_account_id = NULL,
                    stripe_connect_status = NULL
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId });
        }

        public async Task SetStripeTerminalLocationId(Guid tenantId, string locationId)
        {
            const string sql = @"
                UPDATE tenant
                SET stripe_terminal_location_id = @locationId
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, locationId });
        }

        public async Task UpdateServiceCharge(Guid tenantId, int serviceChargeBps, int? monthlyCapCents)
        {
            const string sql = @"
                UPDATE tenant
                SET service_charge_bps = @serviceChargeBps,
                    monthly_service_charge_cap_cents = @monthlyCapCents
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, serviceChargeBps, monthlyCapCents });
        }

        public async Task UpdateLocation(Guid tenantId, string? shippingName, string? addressLine, string? city, string? region,
            string? postalCode, string? country, double? latitude, double? longitude)
        {
            const string sql = @"
                UPDATE tenant
                SET shipping_name = @shippingName,
                    address_line = @addressLine,
                    city = @city,
                    region = @region,
                    postal_code = @postalCode,
                    country = @country,
                    latitude = @latitude,
                    longitude = @longitude
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, shippingName, addressLine, city, region, postalCode, country, latitude, longitude });
        }

        public async Task UpdateHomeContent(Guid tenantId, string? aboutHtml, string? hoursJson,
            string? homeNextUpTitle, Guid[]? homeNextUpEventTypeIds)
        {
            // hours_json is jsonb in Postgres; coerce the text param via cast.
            // home_next_up_event_type_ids is uuid[] — null clears the whitelist (= show all).
            const string sql = @"
                UPDATE tenant
                SET about_html = @aboutHtml,
                    hours_json = COALESCE(@hoursJson::jsonb, '{}'::jsonb),
                    home_next_up_title = @homeNextUpTitle,
                    home_next_up_event_type_ids = @homeNextUpEventTypeIds
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, aboutHtml, hoursJson, homeNextUpTitle, homeNextUpEventTypeIds });
        }

        public async Task UpdateDailyStatus(Guid tenantId, bool? open, string? message)
        {
            const string sql = @"
                UPDATE tenant
                SET daily_status_open = @open,
                    daily_status_message = @message,
                    daily_status_updated_at = CASE WHEN @open IS NULL THEN NULL ELSE now() END
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, open, message });
        }

        public async Task UpdateFooter(Guid tenantId, string? contactEmail, string? phone,
            string? facebook, string? instagram, string? tiktok, string? youtube, string? refundPolicyHtml)
        {
            const string sql = @"
                UPDATE tenant
                SET contact_email = @contactEmail,
                    phone = @phone,
                    social_facebook_url = @facebook,
                    social_instagram_url = @instagram,
                    social_tiktok_url = @tiktok,
                    social_youtube_url = @youtube,
                    refund_policy_html = @refundPolicyHtml
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, contactEmail, phone, facebook, instagram, tiktok, youtube, refundPolicyHtml });
        }

        public async Task UpdateGiftCardSettings(Guid tenantId, bool enabled, int minCents, int maxCents)
        {
            const string sql = @"
                UPDATE tenant
                SET gift_cards_enabled = @enabled,
                    gift_card_min_cents = @minCents,
                    gift_card_max_cents = @maxCents
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled, minCents, maxCents });
        }

        public async Task UpdateRentalsEnabled(Guid tenantId, bool enabled)
        {
            const string sql = "UPDATE tenant SET rentals_enabled = @enabled WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
        }

        public async Task UpdateExtrasEnabled(Guid tenantId, bool enabled)
        {
            const string sql = "UPDATE tenant SET extras_enabled = @enabled WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
        }

        public async Task UpdateSeasonPassesEnabled(Guid tenantId, bool enabled)
        {
            const string sql = "UPDATE tenant SET season_passes_enabled = @enabled WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
        }

        public async Task UpdateCancellationPolicy(
            Guid tenantId, bool allowSelfCancel, bool waitlistEnabled, int waitlistConfirmWindowMinutes)
        {
            const string sql = @"
                UPDATE tenant
                SET allow_self_cancel = @allowSelfCancel,
                    waitlist_enabled = @waitlistEnabled,
                    waitlist_confirm_window_minutes = @waitlistConfirmWindowMinutes
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, allowSelfCancel, waitlistEnabled, waitlistConfirmWindowMinutes });
        }

        public async Task UpdateMembershipSettings(
            Guid tenantId, bool enabled, string name, int priceCents, string durationKind,
            bool requiredForRiders, bool requiredForSpectators)
        {
            const string sql = @"
                UPDATE tenant SET
                    membership_enabled = @enabled,
                    membership_name = @name,
                    membership_price_cents = @priceCents,
                    membership_duration_kind = @durationKind,
                    membership_required_for_riders = @requiredForRiders,
                    membership_required_for_spectators = @requiredForSpectators
                WHERE id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId, enabled, name, priceCents, durationKind,
                requiredForRiders, requiredForSpectators
            });
        }

        public async Task<List<Tenant>> ListAll()
        {
            var sql = $"SELECT {SelectColumns} FROM tenant ORDER BY subdomain";
            var result = await _db.Query<Tenant>(sql);
            return result.ToList();
        }
    }
}
