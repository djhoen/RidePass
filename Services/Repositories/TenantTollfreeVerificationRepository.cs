using Services.Helpers.Interfaces;
using Services.Repositories.Data.SmsData;
using Services.Repositories.Interfaces;

namespace Services.Repositories
{
    public class TenantTollfreeVerificationRepository : ITenantTollfreeVerificationRepository
    {
        private const string Columns = @"
            tenant_id AS TenantId,
            business_name AS BusinessName,
            business_website AS BusinessWebsite,
            business_street_address AS BusinessStreetAddress,
            business_city AS BusinessCity,
            business_state_province_region AS BusinessStateProvinceRegion,
            business_postal_code AS BusinessPostalCode,
            business_country AS BusinessCountry,
            business_contact_first_name AS BusinessContactFirstName,
            business_contact_last_name AS BusinessContactLastName,
            business_contact_email AS BusinessContactEmail,
            business_contact_phone AS BusinessContactPhone,
            notification_email AS NotificationEmail,
            use_case_categories AS UseCaseCategories,
            use_case_summary AS UseCaseSummary,
            production_message_samples AS ProductionMessageSamples,
            opt_in_type AS OptInType,
            opt_in_image_urls AS OptInImageUrls,
            message_volume AS MessageVolume,
            additional_information AS AdditionalInformation,
            twilio_verification_sid AS TwilioVerificationSid,
            status AS Status,
            rejection_reason AS RejectionReason,
            last_submitted_at_utc AS LastSubmittedAtUtc,
            last_status_checked_at_utc AS LastStatusCheckedAtUtc,
            created_at_utc AS CreatedAtUtc,
            updated_at_utc AS UpdatedAtUtc";

        private readonly IDbHelper _db;

        public TenantTollfreeVerificationRepository(IDbHelper db) => _db = db;

        public async Task<TenantTollfreeVerification?> Get(Guid tenantId)
        {
            var sql = $@"
                SELECT {Columns}
                FROM tenant_tollfree_verification
                WHERE tenant_id = @tenantId
                LIMIT 1";
            return (await _db.Query<TenantTollfreeVerification>(sql, new { tenantId })).FirstOrDefault();
        }

        public async Task Upsert(TenantTollfreeVerification v)
        {
            // ON CONFLICT (tenant_id) DO UPDATE — the first save creates the
            // row, subsequent saves overwrite the editable fields. Status /
            // SID / submitted timestamps live in a separate write path so a
            // mid-rejection "save draft" doesn't accidentally clear them.
            const string sql = @"
                INSERT INTO tenant_tollfree_verification (
                    tenant_id, business_name, business_website,
                    business_street_address, business_city, business_state_province_region,
                    business_postal_code, business_country,
                    business_contact_first_name, business_contact_last_name,
                    business_contact_email, business_contact_phone,
                    notification_email,
                    use_case_categories, use_case_summary, production_message_samples,
                    opt_in_type, opt_in_image_urls,
                    message_volume, additional_information,
                    updated_at_utc)
                VALUES (
                    @TenantId, @BusinessName, @BusinessWebsite,
                    @BusinessStreetAddress, @BusinessCity, @BusinessStateProvinceRegion,
                    @BusinessPostalCode, @BusinessCountry,
                    @BusinessContactFirstName, @BusinessContactLastName,
                    @BusinessContactEmail, @BusinessContactPhone,
                    @NotificationEmail,
                    @UseCaseCategories, @UseCaseSummary, @ProductionMessageSamples,
                    @OptInType, @OptInImageUrls,
                    @MessageVolume, @AdditionalInformation,
                    now())
                ON CONFLICT (tenant_id) DO UPDATE SET
                    business_name = EXCLUDED.business_name,
                    business_website = EXCLUDED.business_website,
                    business_street_address = EXCLUDED.business_street_address,
                    business_city = EXCLUDED.business_city,
                    business_state_province_region = EXCLUDED.business_state_province_region,
                    business_postal_code = EXCLUDED.business_postal_code,
                    business_country = EXCLUDED.business_country,
                    business_contact_first_name = EXCLUDED.business_contact_first_name,
                    business_contact_last_name = EXCLUDED.business_contact_last_name,
                    business_contact_email = EXCLUDED.business_contact_email,
                    business_contact_phone = EXCLUDED.business_contact_phone,
                    notification_email = EXCLUDED.notification_email,
                    use_case_categories = EXCLUDED.use_case_categories,
                    use_case_summary = EXCLUDED.use_case_summary,
                    production_message_samples = EXCLUDED.production_message_samples,
                    opt_in_type = EXCLUDED.opt_in_type,
                    opt_in_image_urls = EXCLUDED.opt_in_image_urls,
                    message_volume = EXCLUDED.message_volume,
                    additional_information = EXCLUDED.additional_information,
                    updated_at_utc = now()";
            await _db.Execute(sql, v);
        }

        public async Task SetSubmitted(Guid tenantId, string twilioVerificationSid, string status)
        {
            const string sql = @"
                UPDATE tenant_tollfree_verification
                SET twilio_verification_sid = @twilioVerificationSid,
                    status = @status,
                    rejection_reason = NULL,
                    last_submitted_at_utc = now(),
                    last_status_checked_at_utc = now(),
                    updated_at_utc = now()
                WHERE tenant_id = @tenantId";
            await _db.Execute(sql, new { tenantId, twilioVerificationSid, status });
        }

        public async Task SetStatus(Guid tenantId, string status, string? rejectionReason)
        {
            // Clear rejection_reason whenever the status leaves a *_REJECTED
            // state, so a successful resubmission doesn't leave a stale
            // "Carrier rejected: bad opt-in language" hanging in the UI.
            const string sql = @"
                UPDATE tenant_tollfree_verification
                SET status = @status,
                    rejection_reason = CASE
                        WHEN @status LIKE '%REJECTED' THEN @rejectionReason
                        ELSE NULL
                    END,
                    last_status_checked_at_utc = now(),
                    updated_at_utc = now()
                WHERE tenant_id = @tenantId";
            await _db.Execute(sql, new { tenantId, status, rejectionReason });
        }
    }
}
