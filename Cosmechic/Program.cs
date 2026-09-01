using Cosmechic.Data;
using Cosmechic.Models;
using Cosmechic.Services;
using Cosmechic.Utility;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddScoped<IStripeRefundService, StripeRefundService>();
builder.Services.AddScoped<IShippingCalculator, ShippingCalculator>();
builder.Services.AddScoped<ITaxCalculator, TaxCalculator>();
builder.Services.AddScoped<IOrderLifecycleService, OrderLifecycleService>();
builder.Services.AddScoped<ICheckoutService, OrderCheckoutService>();
builder.Services.AddScoped<IStripeFulfillmentService, StripeFulfillmentService>();
builder.Services.AddScoped<IRefundOrchestrationService, RefundOrchestrationService>();
builder.Services.AddScoped<ICancellationService, CancellationService>();
builder.Services.AddScoped<IReturnService, ReturnService>();
builder.Services.AddScoped<IRestockService, RestockService>();
builder.Services.AddScoped<IAddressService, AddressService>();

builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));

// COSMECHIC-CONTENT-LEGAL-001 (section 6/20/28) : aucune valeur par défaut fabriquée —
// appsettings.json ne fournit que SupportEmail (= Smtp:FromAddress, déjà réel) ; tout le
// reste reste vide/null tant qu'une décision commerciale/juridique réelle n'existe pas.
builder.Services.Configure<BusinessInformationOptions>(builder.Configuration.GetSection("BusinessInformation"));
builder.Services.Configure<CommercePolicyOptions>(builder.Configuration.GetSection("CommercePolicy"));

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

    // COSMECHIC-CONTENT-LEGAL-001 (section 9/25) : le formulaire Contact est public et
    // sans authentification — plafond plus strict qu'AuthSensitive pour limiter l'abus
    // (spam/relais d'email) par IP.
    options.AddPolicy("ContactForm", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
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

// COSMECHIC-RELEASE-CONFIG-001 (section 18) : aucun reverse proxy réel n'est configuré
// pour l'instant (aucune infrastructure n'est connue de ce dépôt). Cette étape prépare
// uniquement le pipeline pour un futur reverse proxy TLS-terminating (Nginx, Cloudflare,
// etc.) SANS faire confiance à un réseau/proxy inventé : ForwardedHeadersOptions.KnownProxies
// et KnownNetworks restent volontairement à leurs valeurs par défaut d'ASP.NET Core, qui ne
// font confiance aux en-têtes X-Forwarded-For/X-Forwarded-Proto que si la connexion directe
// provient du loopback. Sans reverse proxy (ou avec un reverse proxy sur une autre machine),
// ce middleware ne modifie donc rien au comportement actuel. Faire confiance à un proxy
// distant réel nécessitera de configurer explicitement KnownProxies/KnownNetworks une fois
// la topologie réseau de production connue — AWAITING_INFRA_CONFIGURATION (voir
// docs/audits/COSMECHIC-RELEASE-CONFIG-001.md, section 15).
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

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

// COSMECHIC-CATALOG-001 (section 18/19) : routes à slug, enregistrées avant la route
// conventionnelle par défaut. Désambiguïsation avec les noms d'action réels par exclusion
// explicite (lookahead négatif) plutôt que par une contrainte "minuscules uniquement" :
// la contrainte regex intégrée d'ASP.NET Core (RegexInlineRouteConstraint) est
// insensible à la casse par défaut (RegexOptions.IgnoreCase), donc [a-z0-9] y matche
// aussi les majuscules — "/produits/Index" aurait été ambigu malgré tout. Les routes ID
// historiques (ProduitsController.Details(int), CategoriesController.Details(int))
// restent inchangées : aucun lien existant n'est cassé.
app.MapControllerRoute(
    name: "produitBySlug",
    pattern: "produits/{slug:regex(^(?!create$|customer$|delete$|deleteconfirmed$|details$|detailsbyslug$|edit$|index$|itemdetails$|parcategorie$|rechercher$)[a-z0-9]+(-[a-z0-9]+)*$)}",
    defaults: new { controller = "Produits", action = "DetailsBySlug" });

app.MapControllerRoute(
    name: "categorieBySlug",
    pattern: "categories/{slug:regex(^(?!create$|customer$|customerbyslug$|delete$|deleteconfirmed$|details$|edit$|index$)[a-z0-9]+(-[a-z0-9]+)*$)}",
    defaults: new { controller = "Categories", action = "CustomerBySlug" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// COSMECHIC-CATALOG-001 (section 49) : rétro-remplissage Slug/Sku ponctuel, idempotent.
// Ne bloque jamais le démarrage : une base injoignable au boot est journalisée et
// n'empêche pas l'application de démarrer (cohérent avec COSMECHIC-SECURITY-002, section
// gestion d'erreur production — aucune dépendance dure à la base au démarrage).
using (var scope = app.Services.CreateScope())
{
    try
    {
        var catalogContext = scope.ServiceProvider.GetRequiredService<CosmechicsContext>();
        var backfillLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CatalogBackfill");
        await CatalogBackfillService.RunAsync(catalogContext, backfillLogger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Rétro-remplissage catalogue ignoré au démarrage (base injoignable ou non migrée).");
    }
}

// COSMECHIC-COMMERCE-OPERATIONS-001A (section 5) : amorçage des méthodes de livraison et
// taux de taxe par défaut, mêmes garanties que le rétro-remplissage catalogue ci-dessus
// (idempotent, jamais bloquant au démarrage).
using (var scope = app.Services.CreateScope())
{
    try
    {
        var commerceContext = scope.ServiceProvider.GetRequiredService<CosmechicsContext>();
        var seedLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CommerceSeed");
        await CommerceSeedService.RunAsync(commerceContext, seedLogger);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Amorçage commerce (livraison/taxes) ignoré au démarrage (base injoignable ou non migrée).");
    }
}

app.Run();

// Rend la classe Program générée par les instructions de haut niveau accessible à
// WebApplicationFactory<Program> pour les tests d'intégration (COSMECHIC-SECURITY-001).
// N'affecte pas le comportement de l'application.
public partial class Program { }
