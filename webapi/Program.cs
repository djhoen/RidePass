using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using Services.Helpers;
using System.IdentityModel.Tokens.Jwt;
using Services.Helpers.Interfaces;
using Services.Audit;
using Services.Notifications;
using Services.Payments;
using Services.Coupons;
using Services.Repositories;
using Services.Repositories.Data.UserData;
using Services.Repositories.Interfaces;
using Services.Storage;
using System.Text;
using webapi.AuthPolicies;
using webapi.Middleware;
using webapi.Multitenancy;
using webapi.Payments;
using webapi.Storage;

// Don't rewrite short JWT claim names ("role", "sub") into the legacy SOAP URLs.
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // Use the fully-qualified type name for schema ids so nested DTO classes that share a simple name
    // (e.g. several `Option` / `Line` request/response classes) don't collide on the default short name.
    options.CustomSchemaIds(type => (type.FullName ?? type.Name).Replace('+', '.'));
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// RFC 7807 ProblemDetails so unhandled errors return a structured JSON body
// instead of a bare 500. Consumed by the production exception handler below.
builder.Services.AddProblemDetails();

// Liveness health check for load balancers / uptime monitors. Basic check only:
// the AspNetCore.HealthChecks.NpgSql package is not referenced, so we do not add
// a DB probe here (no new NuGet packages). Returns 200 "Healthy" when the app is
// up. Mapped anonymously at GET /api/health below and excluded from tenant resolution.
builder.Services.AddHealthChecks();

// Services and helpers
builder.Services.AddScoped<IDbHelper, DbHelper>();
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ITenantBrandingRepository, TenantBrandingRepository>();
builder.Services.AddScoped<ITenantEventTypeRepository, TenantEventTypeRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<IBlogRepository, BlogRepository>();
builder.Services.AddScoped<IPageRepository, PageRepository>();
builder.Services.AddScoped<IRiderLoampassLinkRepository, RiderLoampassLinkRepository>();
builder.Services.AddScoped<ILoampassRedemptionRepository, LoampassRedemptionRepository>();
builder.Services.AddSingleton<Services.LoamPassMx.ILoamPassMxService, Services.LoamPassMx.LoamPassMxService>();
builder.Services.AddScoped<IBlackoutRepository, BlackoutRepository>();
builder.Services.AddScoped<ISeasonPassRepository, SeasonPassRepository>();
// One place decides a pass holder's discount, so the five tills that ask can't drift on it.
builder.Services.AddScoped<Services.Pricing.ISeasonPassPerkResolver, Services.Pricing.SeasonPassPerkResolver>();
builder.Services.AddScoped<IBikeShopRepository, BikeShopRepository>();
builder.Services.AddScoped<IPlatformPartRepository, PlatformPartRepository>();
builder.Services.AddScoped<IDistributorCredentialRepository, DistributorCredentialRepository>();
// Distributor catalog sync. One source per distributor, resolved by slug. The nightly sweep lives
// in TaskRunner; these registrations serve the settings screen's connect / test / "Sync now".
builder.Services.AddScoped<Services.Distributors.IDistributorCatalogSource, Services.Distributors.QbpCatalogSource>();
// A fake distributor for exercising the sync end to end without a dealer account. Its own
// IsConfigured reads Distributors:EnableSampleSource, which is absent (false) in production, so the
// card never appears there and nobody can pour invented products into a real shop's inventory.
builder.Services.AddScoped<Services.Distributors.IDistributorCatalogSource, Services.Distributors.SampleCatalogSource>();
builder.Services.AddScoped<Services.Distributors.IDistributorSyncService, Services.Distributors.DistributorSyncService>();
// The sync depends on the narrow ICatalogImporter rather than all ~180 members of
// IBikeShopRepository, so it can be unit-tested. Same instance either way.
builder.Services.AddScoped<ICatalogImporter>(sp => sp.GetRequiredService<IBikeShopRepository>());
// The shared parts library's external lookup layer. Disabled is the deliberate default: turning it
// on means caching a vendor's product data, which is a licensing decision (see IPartLookupProvider).
builder.Services.AddScoped<Services.BikeShop.IPartLookupProvider, Services.BikeShop.DisabledPartLookupProvider>();
builder.Services.AddScoped<ITenantCreditRepository, TenantCreditRepository>();
builder.Services.AddScoped<IWristbandRepository, WristbandRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IWaiverRepository, WaiverRepository>();
builder.Services.AddScoped<IWaiverSignRequestRepository, WaiverSignRequestRepository>();
builder.Services.AddScoped<IPackageRepository, PackageRepository>();
builder.Services.AddScoped<IEventTicketTierRepository, EventTicketTierRepository>();
builder.Services.AddScoped<IInstructorRepository, InstructorRepository>();
builder.Services.AddScoped<IEventTicketPurchaseRepository, EventTicketPurchaseRepository>();
builder.Services.AddScoped<IDisputeRepository, DisputeRepository>();
builder.Services.AddScoped<IReportsRepository, ReportsRepository>();
builder.Services.AddScoped<IEndOfDayReportRepository, EndOfDayReportRepository>();
builder.Services.AddScoped<Services.Waivers.IWaiverCheckInGate, Services.Waivers.WaiverCheckInGate>();
builder.Services.AddScoped<Services.Riders.IRiderIdVerification, Services.Riders.RiderIdVerification>();
builder.Services.AddScoped<IRecentSalesRepository, RecentSalesRepository>();
builder.Services.AddScoped<IScheduledTaskRepository, ScheduledTaskRepository>();
// Scheduled-task handlers — add one line per kind. The dispatcher resolves
// them via IEnumerable<IScheduledTaskHandler> and routes by Kind.
builder.Services.AddScoped<Services.Scheduling.IScheduledTaskHandler, Services.Scheduling.Handlers.SendRiderMessageHandler>();
builder.Services.AddScoped<Services.Scheduling.IScheduledTaskHandler, Services.Scheduling.Handlers.SendCampaignHandler>();
builder.Services.AddScoped<Services.Scheduling.ScheduledTaskDispatcher>();
builder.Services.AddScoped<IDiscoverRepository, DiscoverRepository>();
// IP geolocation for the apex Events page (US vs out-of-country branch + radius
// center). Singleton: it holds a pooled static HttpClient and an in-memory cache.
builder.Services.AddSingleton<Services.Geo.IGeoIpService, Services.Geo.GeoIpService>();
builder.Services.AddScoped<INewsletterRepository, NewsletterRepository>();
builder.Services.AddScoped<IEmailCampaignRepository, EmailCampaignRepository>();
builder.Services.AddScoped<IEmailSuppressionRepository, EmailSuppressionRepository>();
builder.Services.AddScoped<IMarketingAutomationRepository, MarketingAutomationRepository>();
builder.Services.AddScoped<Services.TenantSync.ITenantSyncRepository, Services.TenantSync.TenantSyncRepository>();
builder.Services.AddSingleton<webapi.Sync.TenantSyncImageStore>();
builder.Services.AddHttpClient<webapi.Sync.TenantSyncClient>();
builder.Services.AddScoped<webapi.Sync.TenantPromotionService>();
builder.Services.AddScoped<IConcessionRepository, ConcessionRepository>();
builder.Services.AddScoped<ITenantTaxRepository, TenantTaxRepository>();
builder.Services.AddScoped<IQuickBooksRepository, QuickBooksRepository>();
builder.Services.AddScoped<IAccountingEntryRepository, AccountingEntryRepository>();
// QuickBooks Online sync. Options are a singleton (pure config read); the rest are scoped because
// they touch the request-scoped IDbHelper. The nightly sweep itself lives in TaskRunner — these
// registrations serve the settings screen and the manual "Sync now" / re-sync actions.
builder.Services.AddSingleton<Services.QuickBooks.QuickBooksOptions>();
builder.Services.AddScoped<Services.QuickBooks.IQuickBooksTokenService, Services.QuickBooks.QuickBooksTokenService>();
builder.Services.AddScoped<Services.QuickBooks.IQuickBooksApiClient, Services.QuickBooks.QuickBooksApiClient>();
builder.Services.AddScoped<Services.QuickBooks.IQuickBooksSyncService, Services.QuickBooks.QuickBooksSyncService>();
builder.Services.AddScoped<Services.Email.ISesNotificationService, Services.Email.SesNotificationService>();
builder.Services.AddScoped<Services.Email.ISendGridEventService, Services.Email.SendGridEventService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ICashRepository, CashRepository>();
builder.Services.AddScoped<IPlatformSettingRepository, PlatformSettingRepository>();
builder.Services.AddSingleton<webapi.Staging.IStageMirrorService, webapi.Staging.StageMirrorService>();
builder.Services.AddScoped<webapi.Seeding.ITenantSeeder, webapi.Seeding.TenantSeeder>();
builder.Services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
builder.Services.AddScoped<ITenantLedgerRepository, TenantLedgerRepository>();
builder.Services.AddScoped<ITenantPayoutRepository, TenantPayoutRepository>();
builder.Services.AddScoped<IHomePageRepository, HomePageRepository>();
builder.Services.AddScoped<ICouponRepository, CouponRepository>();
builder.Services.AddScoped<IDiscountPresetRepository, DiscountPresetRepository>();
builder.Services.AddScoped<ICouponValidator, CouponValidator>();
builder.Services.AddScoped<IBundledCouponMinter, BundledCouponMinter>();
builder.Services.AddScoped<IGiftCardRepository, GiftCardRepository>();
builder.Services.AddScoped<Services.GiftCards.IGiftCardValidator, Services.GiftCards.GiftCardValidator>();
builder.Services.AddScoped<Services.GiftCards.IGiftCardDeliveryService, Services.GiftCards.GiftCardDeliveryService>();
builder.Services.AddHostedService<webapi.Workers.GiftCardScheduledDeliveryWorker>();
builder.Services.AddScoped<IEventWaitlistRepository, EventWaitlistRepository>();
builder.Services.AddScoped<IEventExtraRepository, EventExtraRepository>();
builder.Services.AddScoped<ITrackFeedbackRepository, TrackFeedbackRepository>();
builder.Services.AddScoped<ITrackLeadRepository, TrackLeadRepository>();
builder.Services.AddScoped<ISurveyRepository, SurveyRepository>();
builder.Services.AddScoped<IMembershipRepository, MembershipRepository>();
builder.Services.AddScoped<Services.Waitlist.IWaitlistPromoter, Services.Waitlist.WaitlistPromoter>();
builder.Services.AddHostedService<webapi.Workers.WaitlistExpiryWorker>();
builder.Services.AddScoped<IFeeCalculator, FeeCalculator>();
builder.Services.AddSingleton<IChargeRouter, ChargeRouter>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IStaffAlertScanRepository, StaffAlertScanRepository>();
builder.Services.AddScoped<IEventSubscriptionRepository, EventSubscriptionRepository>();
builder.Services.AddScoped<IEventNotifier, EventNotifier>();
builder.Services.AddScoped<IAuditLogger, webapi.Helpers.HttpContextAuditLogger>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISmtpEmailer, SmtpEmailer>();
// One confirmation email per event order, from whichever path completed the sale (Stripe, $0
// voucher, gift card, Loam Pass credit, counter cash). Scoped: it reads through the repositories.
builder.Services.AddScoped<Services.Email.IEventOrderConfirmationEmailer, Services.Email.EventOrderConfirmationEmailer>();
builder.Services.AddScoped<Services.Email.IPurchaseConfirmationEmailer, Services.Email.PurchaseConfirmationEmailer>();
builder.Services.AddSingleton<IEmailLinkTokens, EmailLinkTokens>();
// Scoped (not Singleton) because TwilioSmsSender now persists outbound
// messages to tenant_message via ITenantConversationRepository, which is
// scoped per request. The static HttpClient inside the sender continues to
// pool connections regardless of class lifetime.
builder.Services.AddScoped<ISmsSender, TwilioSmsSender>();
builder.Services.AddSingleton<ISmsPricing, SmsPricing>();
builder.Services.AddScoped<Services.Sms.ITwilioSubaccountProvisioner, Services.Sms.TwilioSubaccountProvisioner>();
builder.Services.AddScoped<Services.Sms.ITwilioTollfreeVerifier, Services.Sms.TwilioTollfreeVerifier>();
builder.Services.AddScoped<ITenantBillingEventRepository, TenantBillingEventRepository>();
builder.Services.AddScoped<ITenantConversationRepository, TenantConversationRepository>();
builder.Services.AddScoped<ITenantSmsOptOutRepository, TenantSmsOptOutRepository>();
builder.Services.AddScoped<ITenantTollfreeVerificationRepository, TenantTollfreeVerificationRepository>();
builder.Services.AddScoped<IUpcomingPurchaseRepository, UpcomingPurchaseRepository>();
builder.Services.AddScoped<IPlatformBrandingRepository, PlatformBrandingRepository>();

// Rate limits on the SMS settings endpoints that hit Twilio's master account.
// Partition by request host so a tenant's quota is its subdomain — one bad
// admin can't burn another tenant's budget. Search is generous (6/min),
// Provision is strict (1/min) because each call buys a real phone number.
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    static string PartitionKey(HttpContext ctx)
    {
        var host = ctx.Request.Host.Host;
        if (!string.IsNullOrEmpty(host)) return host;
        return ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    }

    opts.AddPolicy("sms-search", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 6,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    opts.AddPolicy("sms-provision", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(PartitionKey(ctx), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));

    // Manager-PIN verification: coarse per-user throttle on top of the DB-backed lockout. Partition by
    // the authenticated staff user when present so one cashier can't probe PINs in a tight loop.
    opts.AddPolicy("manager-pin", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.User?.FindFirst("UserId")?.Value ?? PartitionKey(ctx),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

// At-rest encryption for sensitive blobs (Twilio subaccount tokens, etc.).
// Throws here at startup if Encryption:KeyBase64 / IvBase64 aren't set, so we
// fail fast rather than discovering it on first Encrypt() call in a request.
{
    var keyB64 = builder.Configuration["Encryption:KeyBase64"];
    var ivB64 = builder.Configuration["Encryption:IvBase64"];
    if (string.IsNullOrWhiteSpace(keyB64) || string.IsNullOrWhiteSpace(ivB64))
    {
        throw new InvalidOperationException(
            "Encryption:KeyBase64 and Encryption:IvBase64 must be configured " +
            "(use `dotnet user-secrets` in dev, env vars in prod).");
    }
    EncryptionHelper.Configure(Convert.FromBase64String(keyB64), Convert.FromBase64String(ivB64));
}

builder.Services.AddSingleton<IPaymentProvider, StripePaymentProvider>();
// Shared Stripe PaymentIntent fulfillment, used by both the webhook and the reconciler.
builder.Services.AddScoped<webapi.Payments.IStripePurchaseFinalizer, webapi.Payments.StripePurchaseFinalizer>();
builder.Services.AddScoped<IPendingPurchaseRepository, PendingPurchaseRepository>();
// Catch-up sweep: finalizes paid-but-pending purchases (missed webhook) and fails
// abandoned ones so their held inventory is released.
builder.Services.AddHostedService<webapi.Workers.PendingPurchaseReconciler>();
// Emails purchasers a "finish your registration" link for paid-but-incomplete event
// tickets, once the checkout is >1h old and at most once per order.
builder.Services.AddHostedService<webapi.Workers.RegistrationReminderWorker>();
builder.Services.AddHostedService<webapi.Workers.ShopServiceReminderWorker>();
builder.Services.AddHostedService<webapi.Workers.MarketingAutomationSweep>();
builder.Services.AddSingleton<webapi.Helpers.IJwtIssuer, webapi.Helpers.JwtIssuer>();
// Image storage: DigitalOcean Spaces (S3) when a bucket is configured, else local disk
// (dev / single-box). Spaces returns absolute bucket URLs, which is what lets a cloned
// staging DB render production's images with no file copy. Singleton: the S3 client pools
// HTTP connections and is thread-safe.
if (!string.IsNullOrWhiteSpace(builder.Configuration["Storage:Spaces:Bucket"]))
{
    builder.Services.AddSingleton<IImageStorage, SpacesImageStorage>();
}
else
{
    builder.Services.AddScoped<IImageStorage, LocalFilesystemImageStorage>();
}

// Password hashing
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<webapi.Security.IManagerPinService, webapi.Security.ManagerPinService>();

// Tenant context (same instance via both interface and concrete type per request)
builder.Services.AddScoped<TenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

// Authorization policies
builder.Services.AddScoped<IAuthorizationHandler, TenantAdminHandler>();
builder.Services.AddScoped<IAuthorizationHandler, TenantPermissionHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, SuperAdminHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(TenantAdminRequirement.PolicyName, p => p.Requirements.Add(new TenantAdminRequirement()));
    options.AddPolicy(SuperAdminRequirement.PolicyName, p => p.Requirements.Add(new SuperAdminRequirement()));
    foreach (var perm in TenantPermissions.All)
    {
        options.AddPolicy(TenantPermissionRequirement.PolicyName(perm),
            p => p.Requirements.Add(new TenantPermissionRequirement(perm)));
    }
    // Any-of policies: one endpoint two different job roles must both reach. Registered from the
    // same table the Policy constants are named for, so the two can't drift apart.
    foreach (var (name, perms) in TenantPermissions.AnyOfPolicies)
    {
        if (name != TenantPermissionRequirement.AnyPolicyName(perms))
            throw new InvalidOperationException(
                $"Any-of policy name '{name}' does not match its permissions {string.Join('|', perms)}.");
        options.AddPolicy(name, p => p.Requirements.Add(new TenantPermissionRequirement(perms)));
    }
});

// CORS - only the apex and its tenant subdomains may call the API with
// credentials. The old SetIsOriginAllowed(_ => true) + AllowCredentials was
// any-origin-with-credentials, which is unsafe. We read the root domain from
// config (Tenant:RootDomain) and allow https://{root} plus any https://*.{root}
// subdomain. In Development we also allow localhost (Vite on :3000) and the
// *.ridepass.local host shape used for local subdomain testing.
var origins = "AllowSpecificOrigins";
var corsRootDomain = (builder.Configuration["Tenant:RootDomain"] ?? "ridepass.io")
    .ToLowerInvariant();
var corsIsDevelopment = builder.Environment.IsDevelopment();
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: origins,
                      policy =>
                      {
                          policy.SetIsOriginAllowed(origin =>
                                {
                                    if (string.IsNullOrWhiteSpace(origin)) return false;
                                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;

                                    var host = uri.Host.ToLowerInvariant();

                                    // Production: https only, the apex itself or any one of its
                                    // subdomains (host == root, or host ends with "." + root).
                                    if (uri.Scheme == Uri.UriSchemeHttps &&
                                        (host == corsRootDomain || host.EndsWith("." + corsRootDomain)))
                                    {
                                        return true;
                                    }

                                    // Development convenience: any localhost port (Vite dev server
                                    // on http://localhost:3000) and the ridepass.local host shape
                                    // (apex + tenant subdomains) on any scheme/port.
                                    if (corsIsDevelopment &&
                                        (host == "localhost" ||
                                         host == "ridepass.local" ||
                                         host.EndsWith(".ridepass.local")))
                                    {
                                        return true;
                                    }

                                    return false;
                                })
                                .AllowAnyMethod()
                                .AllowAnyHeader()
                                // Sliding sessions: the SPA must be able to read the
                                // re-issued token on cross-origin (dev / embed) responses.
                                .WithExposedHeaders(
                                    SlidingSessionMiddleware.HeaderName,
                                    SlidingSessionMiddleware.OriginalHeaderName)
                                .AllowCredentials();
                      });
});

// JWT Authentication
var issuer = builder.Configuration["Jwt:Issuer"]
    ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
var signingKey = builder.Configuration["Jwt:SigningKey"]
    ?? throw new InvalidOperationException("Jwt:SigningKey is not configured.");

builder.Services.AddAuthentication(auth =>
{
    auth.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    auth.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = issuer,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        RoleClaimType = "role"
    };
});

var app = builder.Build();

// Recover the real client IP from nginx. Without this every request appears to come from
// 127.0.0.1 (the proxy, which runs on the same host and proxy_passes to loopback), which is
// exactly what audit_log recorded for its entire history: every row's ip_address was the
// loopback address, so the log could never answer "where was this action taken from?".
//
// Only XForwardedFor is processed on purpose. XForwardedProto would rewrite Request.Scheme
// and XForwardedHost would rewrite the Host, and the subdomain-based tenant resolution and
// URL generation currently work without either; changing them here would be an unrelated
// behavioral risk for no benefit.
//
// Trust is limited to the loopback proxy (the ASP.NET default KnownNetworks already covers
// 127.0.0.0/8 and ::1, which is precisely our nginx). ForwardLimit 1 matches the single hop,
// so a client cannot spoof its own address by sending its own X-Forwarded-For: only the
// last hop, written by our nginx, is honored. If a CDN or load balancer is ever put in
// front, raise ForwardLimit and add its ranges to KnownProxies, or this silently starts
// trusting a client-supplied value.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor,
    ForwardLimit = 1,
});

// Global exception handling. Must run early so it wraps the rest of the
// pipeline. In Development, WebApplication wires the developer exception page
// automatically (full stack traces). In every other environment we convert an
// unhandled exception into an RFC 7807 ProblemDetails (HTTP 500) without leaking
// the stack trace to the caller, and log the exception server-side.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var feature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogError(feature?.Error,
                "Unhandled exception while processing {Method} {Path}",
                context.Request.Method, feature?.Path ?? context.Request.Path.Value);

            var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://datatracker.ietf.org/doc/html/rfc7231#section-6.6.1",
            };

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem);
        });
    });
}

app.UseCors(origins);

app.UseRouting();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Serve uploaded images (logos, heroes) from /uploads/*
app.UseStaticFiles();

// Authenticate first so tenant resolution can read the caller's claims
// (role / tenant_id) and let a tenant's own admins + super admins reach an
// unpublished tenant while the public is blocked.
app.UseAuthentication();

// Slide the session window: an authenticated request re-issues the token (see
// SlidingSessionMiddleware) so active users are never logged out mid-use. Must
// run after UseAuthentication so context.User is populated.
app.UseMiddleware<SlidingSessionMiddleware>();

// Tenant resolution must run before authorization so the permission handlers
// see ITenantContext populated. Excluded for /api/health via UseWhen so the
// health endpoint never gets a 404 from an unknown/inactive/unpublished tenant
// subdomain (UseWhen branches then rejoins, so auth ordering below is preserved).
// Auth still runs before this, and authorization still runs after, as intended.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/health"),
    branch => branch.UseMiddleware<TenantResolutionMiddleware>());

// Must wrap UseAuthorization so it sees the 403 that authorization produces, and so the marker
// the permission handler leaves in HttpContext.Items is still there when the response comes back.
app.UseMiddleware<StaffAccessDenialMiddleware>();

app.UseAuthorization();

app.UseRateLimiter();

// Anonymous liveness endpoint, mapped under /api so nginx proxies it to Kestrel
// (nginx routes /api to the API and everything else to the SPA). AllowAnonymous
// so no JWT is required; tenant resolution is skipped for this path above, so it
// always returns 200 regardless of host/subdomain. Used by the deploy health gate
// and any external uptime monitor.
app.MapHealthChecks("/api/health").AllowAnonymous();

app.MapControllers();

app.Run();
