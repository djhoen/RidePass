namespace Services.TenantSync
{
    /// <summary>
    /// The whitelist of per-tenant CONFIG tables copied when promoting a tenant from stage
    /// to prod, in FK-safe insert order (parents before children). Each entry pairs a table
    /// with the WHERE predicate that scopes its rows to one tenant (@tenantId); child tables
    /// that lack a tenant_id are scoped through their parent.
    ///
    /// Deliberately EXCLUDED (never copied): every *_purchase / *_sale / ledger / payout /
    /// dispute / gift_card (transactional), users / audit_log /
    /// notification* / newsletter* / survey responses / waitlist (runtime + audience), and
    /// all SMS / messaging runtime. Tenant-row env-specific columns (Stripe Connect, Twilio,
    /// Terminal, Loampass, embed/custom-domain, daily status) are reset on import, not here.
    /// </summary>
    public static class TenantSyncTables
    {
        public static readonly IReadOnlyList<(string Table, string Scope)> Config = new (string, string)[]
        {
            ("tenant",                      "id = @tenantId"),
            ("tenant_branding",             "tenant_id = @tenantId"),
            ("tenant_event_type",           "tenant_id = @tenantId"),
            ("tenant_waiver",               "tenant_id = @tenantId"),
            ("blackout",                    "tenant_id = @tenantId"),
            ("event",                       "tenant_id = @tenantId"),
            ("event_ticket_tier",           "tenant_id = @tenantId"),
            ("event_extra_product",         "tenant_id = @tenantId"),
            ("event_extra_variant",         "product_id IN (SELECT id FROM event_extra_product WHERE tenant_id = @tenantId)"),
            ("event_extra_eligibility",     "product_id IN (SELECT id FROM event_extra_product WHERE tenant_id = @tenantId)"),
            ("season_pass_product",         "tenant_id = @tenantId"),
            ("season_pass_event_type_perk", "pass_product_id IN (SELECT id FROM season_pass_product WHERE tenant_id = @tenantId)"),
            ("concession_product",          "tenant_id = @tenantId"),
            ("concession_variant",          "product_id IN (SELECT id FROM concession_product WHERE tenant_id = @tenantId)"),
            ("coupon",                      "tenant_id = @tenantId"),
            ("survey",                      "tenant_id = @tenantId"),
            ("survey_question",             "survey_id IN (SELECT id FROM survey WHERE tenant_id = @tenantId)"),
            ("survey_question_choice",      "question_id IN (SELECT q.id FROM survey_question q JOIN survey s ON s.id = q.survey_id WHERE s.tenant_id = @tenantId)"),
            ("blog_post",                   "tenant_id = @tenantId"),
            ("blog_post_image",             "tenant_id = @tenantId"),
            ("tenant_gallery_image",        "tenant_id = @tenantId"),
            ("tenant_track_graphic",        "tenant_id = @tenantId"),
        };

        // Money-bearing purchase/sale tables checked on import: a tenant with ANY of these
        // (in a money-moved status) can never be overwritten, alongside the ever-published guard.
        public static readonly IReadOnlyList<string> LiveOrderTables = new[]
        {
            "event_ticket_purchase",
            "event_extra_purchase",
            "season_pass_purchase",
            "membership_purchase",
            "concession_sale",
            "gift_card",
        };
    }
}
