using Cosmechic.Data;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Utility;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddScoped<IStripeCheckoutService, StripeCheckoutService>();
builder.Services.AddScoped<ICheckoutService, OrderCheckoutService>();
builder.Services.AddScoped<IStripeFulfillmentService, StripeFulfillmentService>();

builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));

builder.Services.Configure<ImageUploadSettings>(builder.Configuration.GetSection("Uploads"));
builder.Services.AddScoped<IProductImageUploadService, ProductImageUploadService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICspNonceAccessor, CspNonceAccessor>();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDbContext<CosmechicsContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// COSMECHIC-IDENTITY-COMMS-001 : remplace le IEmailSender par défaut (no-op) que
// AddDefaultIdentity vient d'enregistrer via TryAddTransient. Doit être appelé APRÈS
// AddDefaultIdentity : la dernière registration d'un service transient/scoped est celle
// résolue par l'injection de dépendances.
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, SmtpEmailSender>();

builder.Services.AddControllersWithViews();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(100);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// COSMECHIC-SECURITY-002 (section 8) : limite les tentatives sur les routes
// d'authentification sensibles (Login, Register, ForgotPassword,
// ResendEmailConfirmation) pour ralentir le brute-force / credential stuffing,
// sans affecter les autres routes (le webhook Stripe n'a pas cette policy).
// Partitionnement par IP distante : un client abusif ne bloque pas les autres.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthSensitive", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// COSMECHIC-SECURITY-002 (section 12) : en-têtes de sécurité HTTP sur toutes les réponses,
// y compris les pages d'erreur. La CSP script-src est construite à partir d'un inventaire
// réel des sources JS chargées par l'application (voir docs/audits/COSMECHIC-SECURITY-002.md) :
// aucun wildcard, aucun 'unsafe-eval'. Les rares <script> inline utilisent un nonce généré
// par requête (CspNonceAccessor) plutôt que 'unsafe-inline'. style-src conserve 'unsafe-inline'
// car les nombreux attributs style="" inline pré-existants ne peuvent pas porter de nonce
// (limitation de la spec CSP), seule 'unsafe-inline' les couvre.
app.Use(async (context, next) =>
{
    var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    context.Items[CspNonceAccessor.CspNonceItemsKey] = nonce;

    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}' https://cdnjs.cloudflare.com https://cdn.jsdelivr.net https://code.jquery.com https://cdn.startbootstrap.com https://cdn.tiny.cloud; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://cdn.tiny.cloud; " +
        "font-src 'self' https://fonts.gstatic.com https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://cdn.tiny.cloud; " +
        "img-src 'self' data: https://st3.depositphotos.com https://cdn.tiny.cloud; " +
        "connect-src 'self' https://cdn.tiny.cloud; " +
        "frame-ancestors 'none'; " +
        "form-action 'self'; " +
        "base-uri 'self'; " +
        "object-src 'none'";
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["X-Frame-Options"] = "DENY";

    await next();
});

app.UseStaticFiles();
StripeConfiguration.ApiKey = builder.Configuration.GetSection("Stripe:SecretKey").Get<string>();
app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();

// Rend la classe Program générée par les instructions de haut niveau accessible à
// WebApplicationFactory<Program> pour les tests d'intégration (COSMECHIC-SECURITY-001).
// N'affecte pas le comportement de l'application.
public partial class Program { }
