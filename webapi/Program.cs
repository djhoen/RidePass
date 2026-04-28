using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Services.Helpers;
using System.IdentityModel.Tokens.Jwt;
using Services.Helpers.Interfaces;
using Services.Payments;
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
builder.Services.AddScoped<IDayPassProductRepository, DayPassProductRepository>();
builder.Services.AddScoped<IWaiverRepository, WaiverRepository>();
builder.Services.AddScoped<IDayPassPurchaseRepository, DayPassPurchaseRepository>();
builder.Services.AddScoped<IEventTicketTierRepository, EventTicketTierRepository>();
builder.Services.AddScoped<IEventTicketPurchaseRepository, EventTicketPurchaseRepository>();
builder.Services.AddScoped<IDisputeRepository, DisputeRepository>();
builder.Services.AddScoped<IReportsRepository, ReportsRepository>();
builder.Services.AddScoped<IDiscoverRepository, DiscoverRepository>();
builder.Services.AddScoped<INewsletterRepository, NewsletterRepository>();
builder.Services.AddScoped<IEmailCampaignRepository, EmailCampaignRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

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

app.MapControllers();

app.Run();
