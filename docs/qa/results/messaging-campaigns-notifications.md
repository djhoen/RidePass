# QA Results: Messaging (Newsletter/Campaigns, SMS, Notifications, Suppression/Unsubscribe)

Verified against current code as-is (no recent changes in this area). Paths are relative to repo root `C:\Users\djhoe\source\repos\RidePass`.

Counts: 58 PASS, 3 FAIL (MSG11, MSG40, MSG44), 1 NEEDS-LIVE (MSG35), 0 N/A.

| ID | Verdict | Evidence / Notes |
|----|---------|------------------|
| MSG1 | PASS | NewsletterController.cs:41-61 upserts via UpsertFromSignup, borrows user name when none given, returns {subscribed:true}. NewsletterRepository.cs:53-66 ON CONFLICT(tenant_id,email). |
| MSG2 | PASS | NewsletterController.cs:43-46 returns 400 "Subscribing must happen on a tenant subdomain." when !IsResolved. |
| MSG3 | PASS | Me/Subscribe sets source 'account' (NewsletterController.cs:160-161); Me/Status (134-139); Me/Unsubscribe flips unsubscribed_at (182-186). |
| MSG4 | PASS | AddSubscriber source 'admin' (NewsletterController.cs:221); CountActive excludes unsubscribed (NewsletterRepository.cs:99-106). |
| MSG5 | PASS | NewsletterController.cs:231-235 returns 400 demanding opt-in when ConsentConfirmed=false, before any insert. |
| MSG6 | PASS | NewsletterController.cs:245-266: no-@ -> skipped; suppressed set -> suppressed bucket; existing/unsubscribed row -> InsertFromImport returns false -> skipped; new -> added. InsertFromImport never clears unsubscribed_at (NewsletterRepository.cs:71-79). |
| MSG7 | PASS | CampaignController.cs:90-93 returns "Only draft campaigns can be edited." for non-draft. |
| MSG8 | PASS | CampaignController.cs:127-131 returns 400 before any mutation when !_emailer.IsConfigured; campaign stays draft, no send rows, no enqueue. |
| MSG9 | PASS | CampaignController.cs:143-193: ListActiveForSend, blocklist filter, MarkSending, CreateSendRows pending, enqueue send_campaign; RecipientCount excludes suppressed; "(N suppressed skipped)" note (184). |
| MSG10 | PASS | SendCampaignHandler.cs:93 skips non-pending rows; MarkSent recomputes from 'sent' rows (118-119). Retry-safe by design (SMTP delivery itself is runtime). |
| MSG11 | FAIL | SendCampaignHandler.cs:96 writes status "suppressed", but email_campaign_send CHECK allows only ('pending','sent','skipped','failed') (Script0014_Newsletter.sql:55-56); no migration adds 'suppressed' (grep confirms). UpdateSendStatus would throw Postgres 23514 instead of marking the row suppressed. The intended value is almost certainly 'skipped'. |
| MSG12 | PASS | SendCampaignHandler.cs:104-107 sets List-Unsubscribe -> /api/Unsubscribe?token=... and List-Unsubscribe-Post: List-Unsubscribe=One-Click; footer with tenant.DisplayName (108,167-170). |
| MSG13 | PASS | CampaignController.cs:165 60s skew grace -> 'scheduled'; Unschedule cancels task, DeleteSendRows, RevertToDraft (198-229); non-scheduled -> 400 (206-208). |
| MSG14 | PASS | CampaignController.cs:144-147 "No active subscribers..."; 156-159 "Every subscriber is on the suppression list...". |
| MSG15 | PASS | CampaignController.cs:109-112 returns "Cannot delete a campaign that has been sent." for sent/sending; draft (and scheduled) delete. |
| MSG16 | PASS | SendCampaignHandler.cs:134-162 inserts negative email_charge entry, marginal monthly tier (EmailPricing.MarginalChargeCents), catches 23505. Idempotency index uk_ledger_email_charge + entry_kind/source_kind allowed (Script0106_EmailChargeLedger.sql:14,20,23-25). Actual ledger row requires a live SMTP send. |
| MSG17 | PASS | Blank phoneNumber -> 400 "phoneNumber is required." (SmsSettingsController.cs:88-89); audit sms.provision wired (94-97). Actual Twilio provisioning of the number is runtime/NEEDS-LIVE. |
| MSG18 | PASS | Enable before provisioning -> 400 "SMS isn't provisioned yet..." (SmsSettingsController.cs:111-112); after -> SetSmsEnabled(true) + audit sms.enable (114-116). |
| MSG19 | PASS | ResolveCredentials returns messagingServiceSid when set (SmsSender.cs:180-181); SendInternal uses MessagingServiceSid not From (219-226); outbound tenant_message appended (139-155). Twilio call itself is runtime. |
| MSG20 | PASS | Pre-MG tenant: messagingServiceSid null (SmsSender.cs:181) -> From path (223-226); message still recorded (139-155). |
| MSG21 | PASS | ResolveCredentials requires SmsEnabled (SmsSender.cs:169); disabled tenant skips per-tenant creds, falls back to global if IsConfigured else null -> Send returns false (114-115), no tenant_message persisted. |
| MSG22 | PASS | SmsSender.cs:174-189 decrypt-to-empty logs warning and falls through to global (or no-op). |
| MSG23 | PASS | SmsSender.cs:124-131 normalizes, IsOptedOut check returns false + logs "Suppressing SMS... opted out" before Twilio; no message persisted. |
| MSG24 | PASS | TwilioWebhookController.cs:290-298 RecordOptOut on OptOut keyword; TenantSmsOptOutRepository.cs:35-53 sets opted_out=true, opted_out_at_utc, last_keyword; inbound appended (262-271); returns Ok(). Live signed webhook is runtime. |
| MSG25 | PASS | TwilioWebhookController.cs:299-304 RecordOptIn; TenantSmsOptOutRepository.cs:55-72 opted_out=false, opted_in_at_utc, last_keyword. Subsequent IsOptedOut returns false. |
| MSG26 | PASS | SmsKeywords.Classify is whole-body trimmed (SmsKeywords.cs:47-58); "please STOP texting me" -> None. |
| MSG27 | PASS | TwilioWebhookController.cs:305-310 Help -> no state change; message recorded; Ok(). |
| MSG28 | PASS | Bad/missing signature -> 401 (TwilioWebhookController.cs:228-236); InboundSmsWebhookUrl unset -> 401 fail-closed (221-226); unknown subaccount -> Ok() 200 no state change (198-205). |
| MSG29 | PASS | Unique partial index ux_tenant_message_twilio_sid (Script0086_TenantConversation.sql:91-93); handler catches it and treats dup as success 200 (TwilioWebhookController.cs:273-281). |
| MSG30 | PASS | TwilioWebhookController.cs:124-157 bills only on 'delivered'; billed_cents = NumSegments * OutboundPerSegmentCents; UpdateStatusBySid mirrors status. Unique (kind,source_id) (Script0084:47). |
| MSG31 | PASS | RecordIfNew ON CONFLICT(kind,source_id) DO NOTHING (TenantBillingEventRepository.cs:28-35); failed/undelivered branch updates status, no charge (TwilioWebhookController.cs:166-174). |
| MSG32 | PASS | Bad signature -> 401 (TwilioWebhookController.cs:100-106); StatusCallbackUrl unset -> 401 fail-closed (88-96); unknown subaccount -> Ok() no-op (68-77). |
| MSG33 | PASS | SmsSegmentCounter.cs:46-60: GSM <=160 ->1, 161 -> ceil(161/153)=2; UCS-2 <=70 ->1, 71 -> ceil(71/67)=2; GsmExtended chars incl. EUR cost 2 septets (29,49). |
| MSG34 | PASS | SmsPricing.cs:20-25 returns segments*recipients*perSegmentCents; 0 when recipientCount<=0. (TS compose-UI parity not verified here but server logic matches.) |
| MSG35 | NEEDS-LIVE | Draft/submit separation verified: Save (PUT) upserts with no SID (TollfreeVerificationController.cs:67-100); Submit sets SID+status via SetSubmitted (102-146); RefreshStatus (148-182). Status transitions and SID issuance require live Twilio TFV API. |
| MSG36 | PASS | Unprovisioned -> 400 "No SMS provisioning to release." (SmsSettingsController.cs:146-149); audit sms.release wired (163-166). Actual subaccount/number/MG release is runtime/NEEDS-LIVE. |
| MSG37 | PASS | NormalizeE164 (SmsSender.cs:273-283): 10 digits -> +1; leading + -> digits only; 7-digit -> null (reject). Opt-out lookup normalizes the same way before IsOptedOut (124-125). |
| MSG38 | PASS | NotificationController.cs:30-43 ListForUser(userId)/CountUnread(userId); NotificationRepository scoped by recipient_user_id. |
| MSG39 | PASS | MarkRead/MarkAllRead scoped by recipient_user_id = @userId, set is_read+read_at (NotificationRepository.cs:49-63); controller passes caller userId (NotificationController.cs:45-59). |
| MSG40 | FAIL | Expected: non-super-admins get an empty catalog. NotificationKinds.ForRole (NotificationKinds.cs:46-51) returns 4 descriptors for "tenant_admin" (DisputeOpened, DisputeLost, RefundProcessed, PayoutPaid). Only riders/other roles get empty. Code has evolved past the documented super-admin-only catalog. |
| MSG41 | PASS | NotificationService.cs:55-75 always inserts in-app row; email only when IsConfigured && IsEmailEnabled(kind). Actual email send is runtime. |
| MSG42 | PASS | NotificationService.cs:70 gates email on _emailer.IsConfigured; in-app row still inserted. |
| MSG43 | PASS | NotificationPreferenceRepository.cs:23-32 IsEmailEnabled returns true when no row (default enabled). Actual email send is runtime. |
| MSG44 | FAIL | Expected: tenant-admin emits are in-app only. EmitToTenantAdmins DOES send email when IsConfigured && IsEmailEnabled (NotificationService.cs:101-106). EmitToUser is in-app only (110-128) as expected, but the tenant-admin half violates "in-app only." Per-recipient fan-out + independent read state are correct. |
| MSG45 | PASS | NotificationService.cs:53-68 wraps each recipient insert in try/catch, logs warning, continues loop. |
| MSG46 | PASS | UnsubscribeController.cs:38-46 Suppress(tenantId,email,"unsubscribe","marketing","one_click",null); returns {unsubscribed:true}. Marketing scope leaves transactional flowing. |
| MSG47 | PASS | UnsubscribeController.cs:53-61 Suppress(null,email,...,"marketing","one_click_all"); tenant_id NULL = platform-wide marketing. |
| MSG48 | PASS | TryParseUnsubscribe false -> 400 "Unsubscribe link is invalid." on OneClick/AllTracks/Status (UnsubscribeController.cs:40-43,55-57,68-71); no suppression written. |
| MSG49 | PASS | UnsubscribeController.cs:66-85 returns email + tenant DisplayName + unsubscribed flag from IsSuppressed(email,tenantId,marketing:true). |
| MSG50 | PASS | SesNotificationService.cs:104-114 Permanent bounce -> Suppress(null,addr,"bounce","all","ses_bounce",subType); transient ignored (107). |
| MSG51 | PASS | SesNotificationService.cs:115-124 complaint -> Suppress(tenantId,addr,"complaint","marketing",...); tenantId from tag, null -> platform-wide marketing. |
| MSG52 | PASS | SesWebhookController.cs:31-49: WebhookEnabled=false -> 404; BadSignature -> 403; Malformed -> 400; Handled -> 200. |
| MSG53 | PASS | SuppressionController.cs:54-68 Add -> "manual"/"marketing"/"admin"; invalid email -> 400 (62-65); Remove RemoveForTenant scoped by id+tenantId (78); ListForTenant tenant-scoped. |
| MSG54 | PASS | NewsletterController.Unsubscribe/Resubscribe flip unsubscribed_at (84-113); ListActiveForSend excludes unsubscribed AND Send filters email_suppression blocklist (CampaignController.cs:152-154) - both respected. |
| MSG55 | PASS | ListMarketingBlocklist returns lower(email) into OrdinalIgnoreCase HashSet (EmailSuppressionRepository.cs:44-55); Send filters blocklist.Contains(r.Email) case-insensitively. |
| MSG56 | PASS | UpsertFromSignup ON CONFLICT(tenant_id,email) (NewsletterRepository.cs:60) -> independent rows per tenant; ListByTenant/CountActive scoped by tenant_id. uk_newsletter_subscriber (Script0014:17). |
| MSG57 | PASS | All campaign verbs go through GetById(id,tenantId) (EmailCampaignRepository.cs:31-40); cross-tenant id -> NotFound "Campaign not found." (CampaignController.cs:51-60,85-89,104-108,133-137). |
| MSG58 | PASS | ListForTenant WHERE tenant_id = @tenantId excludes tenant_id NULL platform-wide rows (EmailSuppressionRepository.cs:57-71); still enforced at send via ListMarketingBlocklist. |
| MSG59 | PASS | tenant_sms_opt_out keyed (tenant_id,phone) (Script0087:47); IsOptedOut scoped by tenant_id (TenantSmsOptOutRepository.cs:19-33). Tenant B unaffected. |
| MSG60 | PASS | OneClick suppresses only the token's tenantId (UnsubscribeController.cs:44); token is HMAC-signed payload "{tenantId}|{email}" (EmailLinkTokens.cs). Other tenant unaffected; AllTracks for platform-wide. |
| MSG61 | PASS | Send re-checks status != "draft" -> 400 (CampaignController.cs:138-141); CreateSendRows ON CONFLICT(campaign_id,email) DO NOTHING (EmailCampaignRepository.cs:113). Note: no row lock between GetById and MarkSending, so a truly simultaneous double-fire could enqueue two tasks; send-row uniqueness still prevents duplicate recipients and MarkSending is idempotent. |
| MSG62 | PASS | Delete scoped by id + tenant_id (NewsletterRepository.cs:93-97); cross-tenant delete is a no-op, Tenant B row intact. |
