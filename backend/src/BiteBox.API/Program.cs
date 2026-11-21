using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BiteBox.API.Infrastructure;
using BiteBox.Application;
using BiteBox.Application.Abstractions;
using BiteBox.Infrastructure;
using BiteBox.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
// Cookies are HTTPS-only outside Development. Set BiteBox:Security:SecureCookies=false only for a local
// plain-HTTP demo (e.g. docker compose on http://localhost:8080); always keep it true when deployed.
var secureCookies = config.GetValue("BiteBox:Security:SecureCookies", !builder.Environment.IsDevelopment());

// ── Layers ─────────────────────────────────────────────────────────────────────
builder.Services.AddApplication(config);
builder.Services.AddInfrastructure(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ── Authentication: ASP.NET Core Identity with an HttpOnly cookie ───────────────
// The browser stores an encrypted, HttpOnly session cookie that JavaScript cannot read (XSS can't steal it).
// Because cookies are sent automatically, every state-changing request must also carry an anti-forgery token.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "bitebox.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromDays(7);
    options.SlidingExpiration = true;
    // APIs must answer 401/403, not redirect to an HTML login page.
    options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
// Role changes / deactivation rotate the security stamp; re-validate cookies every minute.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin))
    .AddPolicy(Policies.Staff, p => p.RequireRole(Roles.Staff));

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "bitebox.af";
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

// ── MVC, validation, errors ────────────────────────────────────────────────────
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ValidationFilter>();
        options.Filters.Add<AntiforgeryValidationFilter>(); // POST/PUT/PATCH/DELETE need X-XSRF-TOKEN
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = false);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ── Rate limiting ───────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    static string Key(HttpContext ctx) => ctx.User.Identity?.Name ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
    // Per-minute limits, overridable in configuration (the integration tests raise them).
    var authLimit = config.GetValue("BiteBox:RateLimits:AuthPerMinute", 10);
    var aiLimit = config.GetValue("BiteBox:RateLimits:AiPerMinute", 15);
    var checkoutLimit = config.GetValue("BiteBox:RateLimits:CheckoutPerMinute", 10);

    options.AddPolicy(RateLimits.Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "anon", _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy(RateLimits.Ai, ctx => RateLimitPartition.GetFixedWindowLimiter(
        Key(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = aiLimit, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy(RateLimits.Checkout, ctx => RateLimitPartition.GetFixedWindowLimiter(
        Key(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = checkoutLimit, Window = TimeSpan.FromMinutes(1) }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        Key(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = 600, Window = TimeSpan.FromMinutes(1) }));
});

// ── CORS (only needed when the SPA is served from a different origin) ───────────
var allowedOrigins = config.GetSection("BiteBox:Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// ── Health checks ──────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// ── Swagger / OpenAPI ──────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "BiteBox API",
        Version = "v1",
        Description = "REST API for the BiteBox food ordering platform. Authentication uses an HttpOnly cookie; " +
                      "call GET /api/v1/auth/csrf first — the Swagger UI then forwards the XSRF-TOKEN cookie as the X-XSRF-TOKEN header automatically."
    });
    var xml = Path.Combine(AppContext.BaseDirectory, "BiteBox.API.xml");
    if (File.Exists(xml)) c.IncludeXmlComments(xml);
    c.SupportNonNullableReferenceTypes();
});

var app = builder.Build();

// ── Database migration + demo seed ─────────────────────────────────────────────
if (config.GetValue("BiteBox:Database:MigrateOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().MigrateAndSeedAsync();
}

// ── HTTP pipeline (order matters, like a servlet filter chain) ─────────────────
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (secureCookies) app.UseHsts();

if (app.Environment.IsDevelopment() || config.GetValue("BiteBox:Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(ui =>
    {
        ui.DocumentTitle = "BiteBox API";
        // Copy the XSRF cookie into the header so "Try it out" works for POST/PUT/DELETE.
        ui.UseRequestInterceptor("(req) => { const m = document.cookie.match(/(?:^|; )XSRF-TOKEN=([^;]*)/); if (m) req.headers['X-XSRF-TOKEN'] = decodeURIComponent(m[1]); return req; }");
    });
}

app.UseStaticFiles(); // serves /uploads/* product images
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.Run();

/// <summary>Exposed for WebApplicationFactory integration tests.</summary>
public partial class Program;

public static class Policies
{
    public const string Admin = "AdminOnly";
    public const string Staff = "StaffOnly";
}

public static class RateLimits
{
    public const string Auth = "auth";
    public const string Ai = "ai";
    public const string Checkout = "checkout";
}

internal sealed class DatabaseHealthCheck(BiteBoxDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        await db.Database.CanConnectAsync(ct) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Cannot reach MySQL");
}
