using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
using Services.Rewards;
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
builder.Services.AddSwaggerGen();
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
builder.Services.AddScoped<IRiderLoampassLinkRepository, RiderLoampassLinkRepository>();
builder.Services.AddScoped<ILoampassRedemptionRepository, LoampassRedemptionRepository>();
builder.Services.AddSingleton<Services.LoamPassMx.ILoamPassMxService, Services.LoamPassMx.LoamPassMxService>();
builder.Services.AddScoped<IBlackoutRepository, BlackoutRepository>();
builder.Services.AddScoped<ISeasonPassRepository, SeasonPassRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IWaiverRepository, WaiverRepository>();
builder.Services.AddScoped<IEventTicketTierRepository, EventTicketTierRepository>();
builder.Services.AddScoped<IEventTicketPurchaseRepository, EventTicketPurchaseRepository>();
builder.Services.AddScoped<IDisputeRepository, DisputeRepository>();
builder.Services.AddScoped<IReportsRepository, ReportsRepository>();
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
builder.Services.AddScoped<Services.TenantSync.ITenantSyncRepository, Services.TenantSync.TenantSyncRepository>();
builder.Services.AddSingleton<webapi.Sync.TenantSyncImageStore>();
builder.Services.AddHttpClient<webapi.Sync.TenantSyncClient>();
builder.Services.AddScoped<webapi.Sync.TenantPromotionService>();
builder.Services.AddScoped<IConcessionRepository, ConcessionRepository>();
builder.Services.AddScoped<Services.Email.ISesNotificationService, Services.Email.SesNotificationService>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPlatformSettingRepository, PlatformSettingRepository>();
builder.Services.AddSingleton<webapi.Staging.IStageMirrorService, webapi.Staging.StageMirrorService>();
builder.Services.AddScoped<IPasswordResetRepository, PasswordResetRepository>();
builder.Services.AddScoped<ITenantLedgerRepository, TenantLedgerRepository>();
builder.Services.AddScoped<ITenantPayoutRepository, TenantPayoutRepository>();
builder.Services.AddScoped<IHomePageRepository, HomePageRepository>();
builder.Services.AddScoped<ICouponRepository, CouponRepository>();
builder.Services.AddScoped<ICouponValidator, CouponValidator>();
builder.Services.AddScoped<IBundledCouponMinter, BundledCouponMinter>();
builder.Services.AddScoped<IGiftCardRepository, GiftCardRepository>();
builder.Services.AddScoped<Services.GiftCards.IGiftCardValidator, Services.GiftCards.GiftCardValidator>();
builder.Services.AddScoped<Services.GiftCards.IGiftCardDeliveryService, Services.GiftCards.GiftCardDeliveryService>();
builder.Services.AddHostedService<webapi.Workers.GiftCardScheduledDeliveryWorker>();
builder.Services.AddScoped<IRentalRepository, RentalRepository>();
builder.Services.AddScoped<IEventWaitlistRepository, EventWaitlistRepository>();
builder.Services.AddScoped<IEventExtraRepository, EventExtraRepository>();
builder.Services.AddScoped<ITrackFeedbackRepository, TrackFeedbackRepository>();
builder.Services.AddScoped<ITrackLeadRepository, TrackLeadRepository>();
builder.Services.AddScoped<ISurveyRepository, SurveyRepository>();
builder.Services.AddScoped<IMembershipRepository, MembershipRepository>();
builder.Services.AddScoped<Services.Waitlist.IWaitlistPromoter, Services.Waitlist.WaitlistPromoter>();
builder.Services.AddHostedService<webapi.Workers.WaitlistExpiryWorker>();
builder.Services.AddScoped<IFeeCalculator, FeeCalculator>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<INotificationPreferenceRepository, NotificationPreferenceRepository>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IRewardRepository, RewardRepository>();
builder.Services.AddScoped<IRewardEngine, RewardEngine>();
builder.Services.AddScoped<IEventSubscriptionRepository, EventSubscriptionRepository>();
builder.Services.AddScoped<IEventNotifier, EventNotifier>();
builder.Services.AddScoped<IAuditLogger, webapi.Helpers.HttpContextAuditLogger>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISmtpEmailer, SmtpEmailer>();
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

// Tenant resolution must run before authorization so the permission handlers
// see ITenantContext populated. Excluded for /api/health via UseWhen so the
// health endpoint never gets a 404 from an unknown/inactive/unpublished tenant
// subdomain (UseWhen branches then rejoins, so auth ordering below is preserved).
// Auth still runs before this, and authorization still runs after, as intended.
app.UseWhen(
    ctx => !ctx.Request.Path.StartsWithSegments("/api/health"),
    branch => branch.UseMiddleware<TenantResolutionMiddleware>());

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
