using Services.Helpers.Interfaces;
using Services.Repositories.Data.TenantData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantRepository : ITenantRepository
    {
        private const string SelectColumns = @"
            id, subdomain, display_name AS DisplayName, status,
            tenant_type AS TenantType, venue_category AS VenueCategory, timezone,
            require_reservation_for_passes AS RequireReservationForPasses,
            require_emergency_contact AS RequireEmergencyContact,
            allow_event_subscriptions AS AllowEventSubscriptions,
            require_id_at_checkin AS RequireIdAtCheckin,
            stripe_connect_account_id AS StripeConnectAccountId,
            stripe_connect_status AS StripeConnectStatus,
            stripe_charge_mode AS StripeChargeMode,
            stripe_terminal_location_id AS StripeTerminalLocationId,
            stripe_connected_terminal_location_id AS StripeConnectedTerminalLocationId,
            twilio_subaccount_sid AS TwilioSubaccountSid,
            twilio_auth_token_encrypted AS TwilioAuthTokenEncrypted,
            twilio_from_number AS TwilioFromNumber,
            twilio_messaging_service_sid AS TwilioMessagingServiceSid,
            sms_enabled AS SmsEnabled,
            sms_enabled_at_utc AS SmsEnabledAtUtc,
            service_charge_bps AS ServiceChargeBps,
            rental_rider_paid_service_charge_bps AS RentalRiderPaidServiceChargeBps,
            rental_tax_bps AS RentalTaxBps,
            rental_tax_service_charge_taxable AS RentalTaxServiceChargeTaxable,
            rental_insurance_enabled AS RentalInsuranceEnabled,
            rental_insurance_label AS RentalInsuranceLabel,
            rental_insurance_bps AS RentalInsuranceBps,
            monthly_service_charge_cap_cents AS MonthlyServiceChargeCapCents,
            shipping_name AS ShippingName,
            about_html AS AboutHtml,
            hours_json::text AS HoursJson,
            home_next_up_title AS HomeNextUpTitle,
            home_next_up_event_type_ids AS HomeNextUpEventTypeIds,
            home_benefits_html AS HomeBenefitsHtml,
            home_sections_json::text AS HomeSectionsJson,
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
            is_published AS IsPublished,
            first_published_at AS FirstPublishedAt,
            gift_cards_enabled AS GiftCardsEnabled,
            gift_card_min_cents AS GiftCardMinCents,
            gift_card_max_cents AS GiftCardMaxCents,
            rentals_enabled AS RentalsEnabled,
            extras_enabled AS ExtrasEnabled,
            season_passes_enabled AS SeasonPassesEnabled,
            concessions_enabled AS ConcessionsEnabled,
            bike_shop_enabled AS BikeShopEnabled,
            shop_service_reminder_days AS ShopServiceReminderDays,
            shop_ready_notify_email AS ShopReadyNotifyEmail,
            shop_ready_notify_sms AS ShopReadyNotifySms,
            shop_supply_fee_bps AS ShopSupplyFeeBps,
            shop_supply_fee_cap_cents AS ShopSupplyFeeCapCents,
            shop_supply_fee_label AS ShopSupplyFeeLabel,
            shop_labor_rate_cents AS ShopLaborRateCents,
            wristbands_enabled AS WristbandsEnabled,
            trackside_export_enabled AS TracksideExportEnabled,
            blog_enabled AS BlogEnabled,
            dynamic_pricing_enabled AS DynamicPricingEnabled,
            bundled_coupons_enabled AS BundledCouponsEnabled,
            loampass_mx_destination_id AS LoampassMxDestinationId,
            client_type AS ClientType,
            custom_domain AS CustomDomain,
            custom_domain_verified AS CustomDomainVerified,
            embed_enabled AS EmbedEnabled,
            embed_allowed_origins AS EmbedAllowedOrigins,
            external_home_url AS ExternalHomeUrl,
            external_events_url AS ExternalEventsUrl,
            embed_event_target AS EmbedEventTarget,
            allow_self_cancel AS AllowSelfCancel,
            rider_gate_label AS RiderGateLabel,
            spectator_gate_label AS SpectatorGateLabel,
            waitlist_enabled AS WaitlistEnabled,
            waitlist_prepay_enabled AS WaitlistPrepayEnabled,
            waitlist_confirm_window_minutes AS WaitlistConfirmWindowMinutes,
            membership_enabled AS MembershipEnabled,
            membership_name AS MembershipName,
            membership_price_cents AS MembershipPriceCents,
            membership_duration_kind AS MembershipDurationKind,
            membership_required_for_riders AS MembershipRequiredForRiders,
            membership_required_for_spectators AS MembershipRequiredForSpectators,
            created_at AS CreatedAt, updated_at AS UpdatedAt,
            seed_data_populated_at AS SeedDataPopulatedAt";

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
                INSERT INTO tenant (subdomain, display_name, status, tenant_type, venue_category, timezone,
                    client_type, custom_domain, custom_domain_verified, embed_enabled, embed_allowed_origins,
                    external_home_url, external_events_url, embed_event_target,
                    gift_cards_enabled, rentals_enabled, extras_enabled, season_passes_enabled,
                    concessions_enabled, bike_shop_enabled, blog_enabled, dynamic_pricing_enabled, bundled_coupons_enabled,
                    membership_enabled, waitlist_enabled, waitlist_prepay_enabled, allow_self_cancel,
                    trackside_export_enabled)
                VALUES (@Subdomain, @DisplayName, @Status, @TenantType, @VenueCategory, @Timezone,
                    @ClientType, @CustomDomain, @CustomDomainVerified, @EmbedEnabled, @EmbedAllowedOrigins,
                    @ExternalHomeUrl, @ExternalEventsUrl, @EmbedEventTarget,
                    @GiftCardsEnabled, @RentalsEnabled, @ExtrasEnabled, @SeasonPassesEnabled,
                    @ConcessionsEnabled, @BikeShopEnabled, @BlogEnabled, @DynamicPricingEnabled, @BundledCouponsEnabled,
                    @MembershipEnabled, @WaitlistEnabled, @WaitlistPrepayEnabled, @AllowSelfCancel,
                    @TracksideExportEnabled)
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

        public async Task UpdateRequireIdAtCheckin(Guid tenantId, bool require)
        {
            const string sql = "UPDATE tenant SET require_id_at_checkin = @require WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, require });
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

        public async Task SetStripeChargeMode(Guid tenantId, string chargeMode)
        {
            const string sql = @"
                UPDATE tenant
                SET stripe_charge_mode = @chargeMode
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, chargeMode });
        }

        public async Task<Tenant?> GetByStripeConnectAccountId(string accountId)
        {
            var sql = $"SELECT {SelectColumns} FROM tenant WHERE stripe_connect_account_id = @accountId LIMIT 1";
            var result = await _db.Query<Tenant>(sql, new { accountId });
            return result.FirstOrDefault();
        }

        public async Task<Tenant?> GetByTwilioSubaccountSid(string subaccountSid)
        {
            var sql = $"SELECT {SelectColumns} FROM tenant WHERE twilio_subaccount_sid = @subaccountSid LIMIT 1";
            var result = await _db.Query<Tenant>(sql, new { subaccountSid });
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

        public async Task SetStripeConnectedTerminalLocationId(Guid tenantId, string locationId)
        {
            const string sql = @"
                UPDATE tenant
                SET stripe_connected_terminal_location_id = @locationId
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, locationId });
        }

        public async Task SetTwilioCredentials(
            Guid tenantId, string subaccountSid, string authTokenEncrypted,
            string fromNumber, string? messagingServiceSid)
        {
            const string sql = @"
                UPDATE tenant
                SET twilio_subaccount_sid = @subaccountSid,
                    twilio_auth_token_encrypted = @authTokenEncrypted,
                    twilio_from_number = @fromNumber,
                    twilio_messaging_service_sid = @messagingServiceSid,
                    sms_enabled = true,
                    sms_enabled_at_utc = now()
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, subaccountSid, authTokenEncrypted, fromNumber, messagingServiceSid });
        }

        public async Task ClearTwilioCredentials(Guid tenantId)
        {
            const string sql = @"
                UPDATE tenant
                SET twilio_subaccount_sid = NULL,
                    twilio_auth_token_encrypted = NULL,
                    twilio_from_number = NULL,
                    twilio_messaging_service_sid = NULL,
                    sms_enabled = false,
                    sms_enabled_at_utc = NULL
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId });
        }

        public async Task SetSmsEnabled(Guid tenantId, bool enabled)
        {
            // Toggle the on/off switch without clearing credentials — tenant keeps
            // the provisioned number + 10DLC registration while paused.
            const string sql = @"
                UPDATE tenant
                SET sms_enabled = @enabled,
                    sms_enabled_at_utc = CASE WHEN @enabled THEN now() ELSE sms_enabled_at_utc END
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
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

        public async Task UpdateAdminDetails(Guid tenantId, string displayName, string status, string timezone, bool isPublished,
            string? addressLine, string? city, string? region, string? postalCode, string? country,
            double? latitude, double? longitude, string? contactEmail, string? phone, string? loampassMxDestinationId,
            string clientType, string? customDomain, bool customDomainVerified, bool embedEnabled,
            string[]? embedAllowedOrigins, string? externalHomeUrl, string? externalEventsUrl, string embedEventTarget)
        {
            const string sql = @"
                UPDATE tenant
                SET display_name = @displayName,
                    status = @status,
                    timezone = @timezone,
                    is_published = @isPublished,
                    address_line = @addressLine,
                    city = @city,
                    region = @region,
                    postal_code = @postalCode,
                    country = @country,
                    latitude = @latitude,
                    longitude = @longitude,
                    contact_email = @contactEmail,
                    phone = @phone,
                    loampass_mx_destination_id = @loampassMxDestinationId,
                    client_type = @clientType,
                    custom_domain = @customDomain,
                    custom_domain_verified = @customDomainVerified,
                    embed_enabled = @embedEnabled,
                    embed_allowed_origins = @embedAllowedOrigins,
                    external_home_url = @externalHomeUrl,
                    external_events_url = @externalEventsUrl,
                    embed_event_target = @embedEventTarget
                WHERE id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId, displayName, status, timezone, isPublished, addressLine, city, region,
                postalCode, country, latitude, longitude, contactEmail, phone, loampassMxDestinationId,
                clientType, customDomain, customDomainVerified, embedEnabled, embedAllowedOrigins,
                externalHomeUrl, externalEventsUrl, embedEventTarget,
            });
        }

        // Super-admin feature toggles. Narrow to just the boolean flags so it never
        // touches the dependent config (gift-card min/max, membership price, etc.),
        // which the tenant manages on their own Settings -> Features page.
        public async Task UpdateFeatures(Guid tenantId, bool giftCardsEnabled, bool extrasEnabled,
            bool seasonPassesEnabled, bool concessionsEnabled, bool blogEnabled, bool membershipEnabled,
            bool waitlistEnabled, bool waitlistPrepayEnabled, bool allowSelfCancel, bool dynamicPricingEnabled,
            bool bundledCouponsEnabled, bool bikeShopEnabled)
        {
            const string sql = @"
                UPDATE tenant
                SET gift_cards_enabled = @giftCardsEnabled,
                    extras_enabled = @extrasEnabled,
                    season_passes_enabled = @seasonPassesEnabled,
                    concessions_enabled = @concessionsEnabled,
                    blog_enabled = @blogEnabled,
                    membership_enabled = @membershipEnabled,
                    waitlist_enabled = @waitlistEnabled,
                    waitlist_prepay_enabled = @waitlistPrepayEnabled,
                    allow_self_cancel = @allowSelfCancel,
                    dynamic_pricing_enabled = @dynamicPricingEnabled,
                    bundled_coupons_enabled = @bundledCouponsEnabled,
                    bike_shop_enabled = @bikeShopEnabled
                WHERE id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId, giftCardsEnabled, extrasEnabled, seasonPassesEnabled,
                concessionsEnabled, blogEnabled, membershipEnabled, waitlistEnabled, waitlistPrepayEnabled, allowSelfCancel,
                dynamicPricingEnabled, bundledCouponsEnabled, bikeShopEnabled,
            });
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
            string? homeNextUpTitle, Guid[]? homeNextUpEventTypeIds,
            string? homeBenefitsHtml, string? homeSectionsJson)
        {
            // hours_json / home_sections_json are jsonb in Postgres; coerce the text params via cast.
            // home_next_up_event_type_ids is uuid[] — null clears the whitelist (= show all).
            const string sql = @"
                UPDATE tenant
                SET about_html = @aboutHtml,
                    hours_json = COALESCE(@hoursJson::jsonb, '{}'::jsonb),
                    home_next_up_title = @homeNextUpTitle,
                    home_next_up_event_type_ids = @homeNextUpEventTypeIds,
                    home_benefits_html = @homeBenefitsHtml,
                    home_sections_json = COALESCE(@homeSectionsJson::jsonb, '{}'::jsonb)
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, aboutHtml, hoursJson, homeNextUpTitle,
                homeNextUpEventTypeIds, homeBenefitsHtml, homeSectionsJson });
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

        // One setter for the shop's customer-notification policy: the three settings are edited
        // together on the same screen, so saving them together keeps the screen and the row honest.
        public async Task UpdateShopNotificationSettings(
            Guid tenantId, bool readyEmail, bool readySms, int reminderDays)
        {
            const string sql = @"
                UPDATE tenant
                SET shop_ready_notify_email = @readyEmail,
                    shop_ready_notify_sms = @readySms,
                    shop_service_reminder_days = @reminderDays
                WHERE id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId, readyEmail, readySms, reminderDays = Math.Clamp(reminderDays, 0, 730),
            });
        }

        // Only the split moves here. The rate is service_charge_bps, shared with events, and is not
        // editable from the rentals screen.
        public async Task UpdateRentalSettings(Guid tenantId, int riderPaidBps, int? taxBps, bool serviceChargeTaxable,
            bool insuranceEnabled, string? insuranceLabel, int insuranceBps)
        {
            const string sql = @"
                UPDATE tenant
                SET rental_rider_paid_service_charge_bps = @riderPaidBps,
                    rental_tax_bps = @taxBps,
                    rental_tax_service_charge_taxable = @serviceChargeTaxable,
                    rental_insurance_enabled = @insuranceEnabled,
                    rental_insurance_label = @insuranceLabel,
                    rental_insurance_bps = @insuranceBps
                WHERE id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId,
                riderPaidBps = Math.Clamp(riderPaidBps, 0, 10000),
                // Null stays null: it is what distinguishes "never set" from "deliberately 0%".
                taxBps = taxBps.HasValue ? Math.Clamp(taxBps.Value, 0, 10000) : (int?)null,
                serviceChargeTaxable,
                insuranceEnabled,
                insuranceLabel = string.IsNullOrWhiteSpace(insuranceLabel) ? null : insuranceLabel.Trim(),
                insuranceBps = Math.Clamp(insuranceBps, 0, 10000),
            });
        }

        public async Task UpdateShopSupplyFee(Guid tenantId, int bps, int? capCents, string label)
        {
            const string sql = @"
                UPDATE tenant
                SET shop_supply_fee_bps = @bps,
                    shop_supply_fee_cap_cents = @capCents,
                    shop_supply_fee_label = @label
                WHERE id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId,
                bps = Math.Clamp(bps, 0, 5000),
                capCents = capCents.HasValue ? Math.Max(0, capCents.Value) : (int?)null,
                label = string.IsNullOrWhiteSpace(label) ? "Shop supplies" : label.Trim(),
            });
        }

        public async Task UpdateShopLaborRate(Guid tenantId, int? rateCents)
        {
            const string sql = "UPDATE tenant SET shop_labor_rate_cents = @rateCents WHERE id = @tenantId";
            await _db.Execute(sql, new
            {
                tenantId,
                // Null stays null (no rate set). A sent value is floored at 0 and capped at a sane
                // ceiling so a fat-fingered rate can't bill thousands per hour.
                rateCents = rateCents.HasValue ? Math.Clamp(rateCents.Value, 0, 100_000) : (int?)null,
            });
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

        public async Task UpdateBikeShopEnabled(Guid tenantId, bool enabled)
        {
            const string sql = "UPDATE tenant SET bike_shop_enabled = @enabled WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
        }

        public async Task<bool> HasSpectatorTiers(Guid tenantId)
        {
            // "Has ever sold spectator passes": any spectator-audience or spectator-pass
            // tier on any of the tenant's events, active or not. Drives the Spectator
            // Report nav visibility, so it deliberately never un-shows once true.
            const string sql = @"
                SELECT EXISTS (
                    SELECT 1 FROM event_ticket_tier
                    WHERE tenant_id = @tenantId
                      AND (audience = 'spectator' OR kind = 'spectator_pass'))";
            return (await _db.Query<bool>(sql, new { tenantId })).First();
        }

        public async Task UpdateTracksideExportEnabled(Guid tenantId, bool enabled)
        {
            const string sql = "UPDATE tenant SET trackside_export_enabled = @enabled WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
        }

        public async Task UpdateWristbandsEnabled(Guid tenantId, bool enabled)
        {
            const string sql = "UPDATE tenant SET wristbands_enabled = @enabled WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
        }

        public async Task UpdateConcessionsEnabled(Guid tenantId, bool enabled)
        {
            const string sql = "UPDATE tenant SET concessions_enabled = @enabled WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
        }

        public async Task UpdateBlogEnabled(Guid tenantId, bool enabled)
        {
            const string sql = "UPDATE tenant SET blog_enabled = @enabled WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, enabled });
        }

        // Tenant-facing. Deliberately does NOT touch waitlist_enabled: that's super-admin-only and is
        // set by UpdateFeatureFlags. See Script0180.
        public async Task UpdateCancellationPolicy(
            Guid tenantId, bool allowSelfCancel, int waitlistConfirmWindowMinutes)
        {
            const string sql = @"
                UPDATE tenant
                SET allow_self_cancel = @allowSelfCancel,
                    waitlist_confirm_window_minutes = @waitlistConfirmWindowMinutes
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, allowSelfCancel, waitlistConfirmWindowMinutes });
        }

        public async Task UpdateGateLabels(Guid tenantId, string? riderGateLabel, string? spectatorGateLabel)
        {
            const string sql = @"
                UPDATE tenant
                SET rider_gate_label = @riderGateLabel,
                    spectator_gate_label = @spectatorGateLabel
                WHERE id = @tenantId";
            await _db.Execute(sql, new { tenantId, riderGateLabel, spectatorGateLabel });
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
