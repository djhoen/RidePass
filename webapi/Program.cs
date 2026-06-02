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

// Services and helpers
builder.Services.AddScoped<IDbHelper, DbHelper>();
builder.Services.AddScoped<ITenantRepository, TenantRepository>();
builder.Services.AddScoped<ITenantBrandingRepository, TenantBrandingRepository>();
builder.Services.AddScoped<ITenantEventTypeRepository, TenantEventTypeRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<IBlackoutRepository, BlackoutRepository>();
builder.Services.AddScoped<IPassProductRepository, PassProductRepository>();
builder.Services.AddScoped<ISeasonPassRepository, SeasonPassRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IWaiverRepository, WaiverRepository>();
builder.Services.AddScoped<IPassPurchaseRepository, PassPurchaseRepository>();
builder.Services.AddScoped<IEventTicketTierRepository, EventTicketTierRepository>();
builder.Services.AddScoped<IEventTicketPurchaseRepository, EventTicketPurchaseRepository>();
builder.Services.AddScoped<IDisputeRepository, DisputeRepository>();
builder.Services.AddScoped<IReportsRepository, ReportsRepository>();
builder.Services.AddScoped<IRecentSalesRepository, RecentSalesRepository>();
builder.Services.AddScoped<IScheduledTaskRepository, ScheduledTaskRepository>();
// Scheduled-task handlers — add one line per kind. The dispatcher resolves
// them via IEnumerable<IScheduledTaskHandler> and routes by Kind.
builder.Services.AddScoped<Services.Scheduling.IScheduledTaskHandler, Services.Scheduling.Handlers.SendRiderMessageHandler>();
builder.Services.AddScoped<Services.Scheduling.ScheduledTaskDispatcher>();
builder.Services.AddScoped<IDiscoverRepository, DiscoverRepository>();
builder.Services.AddScoped<INewsletterRepository, NewsletterRepository>();
builder.Services.AddScoped<IEmailCampaignRepository, EmailCampaignRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
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
builder.Services.AddSingleton<webapi.Helpers.IJwtIssuer, webapi.Helpers.JwtIssuer>();
builder.Services.AddScoped<IImageStorage, LocalFilesystemImageStorage>();

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

// CORS - dev defaults; tighten for prod via config
var origins = "AllowSpecificOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: origins,
                      policy =>
                      {
                          policy.SetIsOriginAllowed(_ => true).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
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

// Tenant resolution must run before auth so controllers see ITenantContext populated.
app.UseMiddleware<TenantResolutionMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseRateLimiter();

app.MapControllers();

app.Run();
