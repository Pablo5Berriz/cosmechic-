# COSMECHIC-SECURITY-002 — Durcissement applicatif, upload, dépendances et bases de sécurité

- **Lot** : COSMECHIC-SECURITY-002
- **Base de départ** : `9c2c037dbbcf241fa92cb887b0f7a8afbb9d0c2e` (COSMECHIC-IDENTITY-COMMS-001, PASS)
- **Portée** : upload sécurisé, remédiation NuGet, doublon `StripeSettings` (CS0436), secrets, cookies/session, CSRF, rate limiting, énumération de comptes, en-têtes/CSP, gestion d'erreur production, logs, autorisation admin, mass assignment, open redirect, XSS, injection SQL, tests de sécurité, régression complète.
- **Hors scope, volontairement non touché** : COSMECHIC-CATALOG-001 (recherche, filtres, tri, slugs, images multiples) — non commencé.

## 1. Upload d'images (SEC-007)

**Avant** : `ProduitsController`/`CategoriesController` construisaient le nom de fichier stocké par concaténation directe (`Guid.NewGuid() + "_" + Image.FileName`) — le nom client (donc son extension) atteignait le système de fichiers sans validation d'extension, de type MIME ni de signature binaire, et sans limite de taille explicite.

**Après** — `Cosmechic/Services/ProductImageUploadService.cs` (nouveau) :
- Nom de fichier stocké **entièrement généré côté serveur** (`{Guid:N}{extension}`) : aucun caractère client n'atteint le chemin final.
- Allowlist stricte extension → MIME attendu : `.jpg`/`.jpeg`→`image/jpeg`, `.png`→`image/png`, `.webp`→`image/webp`. SVG, exécutables, scripts explicitement exclus.
- Vérification de signature binaire (magic bytes) après validation extension+Content-Type : JPEG (`FF D8 FF`), PNG (8 octets), WEBP (`RIFF`+taille+`WEBP`). Un fichier `.cshtml` renommé `.jpg` est rejeté (`InvalidSignature`).
- Taille maximale configurable (`Uploads:MaxFileSizeBytes`, 5 Mo par défaut), testée à 0/à la limite exacte/juste au-dessus.
- `FileMode.CreateNew` : aucun écrasement silencieux possible.
- Appliqué aux deux seuls points d'upload existants (`ProduitsController`, `CategoriesController`), tous deux déjà `[Authorize(Roles = "Admin")]` + `[ValidateAntiForgeryToken]`.

`ImageUploadServiceTests.cs` (15 tests) : fichier vide, taille au-dessus/exactement à la limite, extensions interdites (`.exe`/`.svg`/`.php`/`.cshtml`/vide), Content-Type incohérent, malware renommé, chemins avec `../`/`..\\`/`/` (nom stocké prouvé être un GUID sans trace du nom d'origine), unicité des noms générés, faux PNG à contenu JPEG.

## 2. Dépendances NuGet

**Recertification** (`dotnet list package --vulnerable --include-transitive`) : `MailKit`/`MimeKit` (Moderate), `Microsoft.Build` (High), `NuGet.Packaging`/`NuGet.Protocol` (Low), `OpenMcdf` (Moderate ×2), `SQLitePCLRaw.lib.e_sqlite3` (High), `System.Text.Json` (High).

**Root cause tracée** (`obj/project.assets.json`) : trois paquets directs jamais utilisés dans le code (`git grep` exhaustif, zéro occurrence) tiraient toute la chaîne vulnérable :
- `Microsoft.VisualStudio.Web.CodeGeneration.Design` → `Microsoft.Build` (High), `NuGet.Packaging`/`NuGet.Protocol` (Low), une partie de `System.Text.Json` (High).
- `FileSignatures` → `OpenMcdf` (Moderate ×2). Devenu redondant avec `ProductImageUploadService` qui implémente sa propre vérification de signature.
- `DocumentFormat.OpenXml` → aucune vulnérabilité directe mais confirmé totalement inutilisé.
- `Microsoft.EntityFrameworkCore.Sqlite` (confirmé inutilisé — `grep -rn "Sqlite"` sur `*.cs` : zéro résultat, l'app est exclusivement SQL Server) → `SQLitePCLRaw.lib.e_sqlite3` (High).

**Remédiation appliquée** :
| PACKAGE | DIRECT/TRANSITIVE | AVANT | APRÈS | ACTION |
|---|---|---|---|---|
| DocumentFormat.OpenXml | Direct | 3.2.0 | *(retiré)* | Suppression — inutilisé |
| FileSignatures | Direct | 5.1.1 | *(retiré)* | Suppression — inutilisé, redondant |
| Microsoft.VisualStudio.Web.CodeGeneration.Design | Direct | 8.0.7 | *(retiré)* | Suppression — outil de scaffold, jamais invoqué en runtime |
| Microsoft.EntityFrameworkCore.Sqlite | Direct | 8.0.12 | *(retiré)* | Suppression — app 100% SQL Server |
| MailKit | Direct | 4.10.0 | 4.17.0 | Mise à jour minimale — activement utilisé par `SmtpEmailSender` |
| MimeKit | Direct | 4.10.0 | 4.17.0 | Idem |

Résultat post-remédiation : `dotnet list package --vulnerable --include-transitive` → **0 vulnérabilité** sur les 3 projets (Cosmechic, Cosmechic.Utility, Cosmechic.Tests). Aucune mise à jour majeure arbitraire, aucun `update everything`.

## 3. Doublon `StripeSettings` (CS0436)

Deux fichiers `StripeSettings.cs` existaient : `Cosmechic.Utility/StripeSettings.cs` (le vrai fichier du projet `Cosmechic.Utility`, référencé via `ProjectReference`) et `Cosmechic/Cosmechic.Utility/StripeSettings.cs` (un dossier orphelin *à l'intérieur* de l'arborescence du projet web, capté par le globbing SDK implicite de `Cosmechic.csproj`).

**Preuve du fichier canonique** : build non-incrémental (`--no-incremental`) → `CS0436: ... Using the type defined in '.../Cosmechic/Cosmechic.Utility/StripeSettings.cs'` — c'est-à-dire que le doublon orphelin gagnait silencieusement sur le vrai type importé du projet référencé, dans `Program.cs` et `StripeWebhookController.cs`. Le fichier orphelin a été supprimé (`git rm`) ; `Cosmechic.Utility/StripeSettings.cs` reste l'unique source. `CS0436=0` confirmé après suppression, 92→94 tests toujours PASS.

## 4. Secrets

Scan exhaustif (working tree + historique git depuis le commit d'import) : `Stripe:SecretKey`, `Stripe:WebhookSecret`, `Smtp:Password` sont et ont toujours été des chaînes vides dans `appsettings.json`. Seule valeur non vide : `Stripe:PublishableKey` (clé publique Stripe, conçue pour être publique). Aucune clé privée, aucun `sk_live`/`sk_test`/`whsec_` réel, aucune chaîne de connexion avec mot de passe embarqué (`Trusted_Connection=True`). Rien à corriger.

## 5. Cookies et session

Revue des defaults ASP.NET Core Identity (cookie d'authentification) et `AddSession` (déjà `HttpOnly=true`, `IsEssential=true` depuis SECURITY-001). `CookieSecurePolicy.SameAsRequest` (défaut) est correct ici : combiné à `UseHttpsRedirection`/`UseHsts` en production, le cookie est marqué `Secure` dès que la requête passe en HTTPS — pas de changement nécessaire. Le compteur de panier en session ne porte aucune donnée sensible (juste un entier, recalculé depuis la base si absent) : pas de risque de fixation de session exploitable identifié. Aucun changement appliqué (defaults déjà corrects).

## 6. CSRF et audit GET-mutation

Trois lacunes trouvées et corrigées :
- `CartController.SummaryPOST` (création de session de paiement) : `[HttpPost]` sans `[ValidateAntiForgeryToken]`.
- `ProduitsController.ItemDetails(ShoppingCart)` (ajout panier) : idem.
- `HomeController.AjouterAuPanier` : idem (action actuellement non appelée par aucune vue, durcie par cohérence).

Vérifié empiriquement (test avec la vraie pipeline antiforgery, sans le double `NoOpAntiforgery` des tests) qu'un `<form method="post">` sans attribut `asp-*` reçoit bien le jeton caché automatiquement (`FormTagHelper` ne nécessite pas d'attribut `asp-*` pour s'activer) — donc aucune modification de vue nécessaire, uniquement l'attribut manquant côté contrôleur.

Audit GET-mutation : tous les appels `_context.SaveChanges(Async)`/`Add`/`Update`/`Remove` de l'application recensés et confirmés être exclusivement dans des actions `[HttpPost]` déjà protégées. `StripeWebhookController.Handle` reste volontairement sans jeton CSRF (authentification par signature HMAC Stripe, pas un utilisateur Cosmechic).

## 7. Rate limiting

`Microsoft.AspNetCore.RateLimiting` (intégré .NET 8, aucun paquet ajouté) : policy `AuthSensitive` (fenêtre fixe, 10 requêtes/minute, partitionnée par IP distante) appliquée via `[EnableRateLimiting("AuthSensitive")]` sur `LoginModel`, `RegisterModel`, `ForgotPasswordModel`, `ResendEmailConfirmationModel`. Aucune policy globale : le webhook Stripe (retries légitimes) n'est pas affecté. Testé : dépassement → 429 ; route hors périmètre → jamais 429.

## 8. Énumération de comptes

`ForgotPassword`, `ResendEmailConfirmation`, `Login` déjà corrigés en IDENTITY-COMMS-001 (message identique que le compte existe ou non). Revérifié ligne par ligne ce lot : toujours correct, aucune régression, aucun changement nécessaire.

## 9. En-têtes de sécurité et CSP

Inventaire réel de toutes les sources script/style/font/image chargées (`grep` exhaustif sur les vues) avant écriture de la policy — aucune source devinée :
- **script-src** : `'self'` + nonce par requête (voir ci-dessous) + `cdnjs.cloudflare.com`, `cdn.jsdelivr.net`, `code.jquery.com`, `cdn.startbootstrap.com`, `cdn.tiny.cloud` (TinyMCE, actif globalement via `site.js` sur tout `<textarea>`).
- **style-src** : `'self' 'unsafe-inline'` (nombreux attributs `style=""` pré-existants, sans échappatoire CSP par nonce pour les attributs inline) + `fonts.googleapis.com`, `cdn.jsdelivr.net`, `cdnjs.cloudflare.com`, `cdn.tiny.cloud`.
- **font-src**, **img-src** (avec `data:` — CSS admin embarquant des icônes en base64), **connect-src** : construits sur le même principe.
- `frame-ancestors 'none'`, `form-action 'self'`, `base-uri 'self'`, `object-src 'none'`. Aucun `*`, aucun `unsafe-eval`.

4 blocs `<script>` inline recensés (`Diagrammes.cshtml`, `Dashboard.cshtml`, `ItemDetails.cshtml`, `About.cshtml`) : reçoivent un nonce généré par requête (`CspNonceAccessor`, service scoped injecté via `_ViewImports.cshtml`) plutôt que `'unsafe-inline'`. Middleware dédié dans `Program.cs` (avant `UseStaticFiles`, pour couvrir aussi les pages d'erreur) pose également `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`. Vérifié par requête réelle (`SecurityHeadersTests.cs`, 2 tests) que la CSP ne casse rien (régression complète 116/116 PASS).

## 10. Gestion d'erreur en production (bug connu : page d'erreur qui plante si SQL Server est injoignable)

**Reproduit** : `_Layout.cshtml` invoque `ShoppingCartViewComponent` sur **toutes** les pages, y compris `/Home/Error`. Pour un utilisateur authentifié sans compteur de panier déjà en session, ce composant interroge `CosmechicsContext` sans filet — si la base est injoignable, l'exception se produit **à l'intérieur du pipeline de gestion d'erreur lui-même**, provoquant un 500 brut sans page conviviale (confirmé avec une connexion SQL Server pointée vers une adresse injoignable, `DatabaseOutageTests.ErrorPage_RendersGracefully_WhenDatabaseIsUnreachable` échouait avant correctif : `InternalServerError` au lieu de `OK`).

**Corrigé** : `ShoppingCartViewComponent` capture désormais l'exception, journalise, et dégrade vers un compteur à 0 plutôt que de la laisser se propager. Revalidé : la page d'erreur elle-même (accès direct) et le chemin `UseExceptionHandler` complet (page ordinaire qui échoue → redirection interne → page d'erreur) rendent tous deux un contenu convivial, sans fuite de `SqlException`/`Microsoft.Data.SqlClient` dans le corps de la réponse (`DatabaseOutageTests.cs`, 2 tests).

## 11. Logs

Audit de tous les appels `_logger.Log*` de l'application (Stripe, email, Identity scaffoldé, nouveau composant panier) : uniquement des identifiants opérationnels (EventId, EventType, OrderId, ProduitId, montants/devises pour les alertes d'incohérence, UserId). Aucun mot de passe, jeton, ou secret journalisé. `SmtpEmailSender` journalise l'adresse destinataire et le sujet (utile au support, non un secret) — conservé tel quel.

## 12. Autorisation admin

Matrice complète relue pour tous les contrôleurs. Une lacune réelle trouvée : `CategoriesController.Index` (vue de gestion avec liens Ajouter/Modifier/Supprimer) n'avait **aucune** restriction — accessible anonymement, alors que `Details`/`Create`/`Edit`/`Delete` du même contrôleur sont `Admin`-only et que la navigation client passe par l'action `Customer` séparée. Corrigé : `[Authorize(Roles = "Admin")]` ajouté. Petite incohérence UX corrigée en passant : `Produits/Index.cshtml` affichait le bouton "Ajouter un produit" même aux non-admins (l'action sous-jacente était déjà protégée, donc pas une faille, juste un affichage trompeur). Régression : `AdminSurfaceAuthorizationTests.cs` (3 tests).

## 13. Mass assignment

Revue de tous les `[Bind(...)]`/binding de modèle complet. Un point durci : `ProduitsController.ItemDetails(ShoppingCart)` liait le modèle complet sans allowlist — un client pourrait en théorie poster des champs `Produit.*` (propriété de navigation) que le binder tenterait de matérialiser en entité non suivie. Restreint à `[Bind(nameof(ShoppingCart.ProduitId), nameof(ShoppingCart.Count))]` ; `ApplicationUserId` était déjà réassigné depuis l'utilisateur authentifié après binding, jamais fait confiance à la requête. Les contrôleurs admin-only (`OrderDetails`, `OrderHeaders`, `Produits`, `Categories`) liant `Price`/`Stock`/`OrderStatus`/etc. sont des CRUD admin légitimes, pas des lacunes.

## 14. Open redirect

Un seul `Redirect(` brut dans toute l'application (`DeletePersonalData.cshtml.cs`, constante `"~/"`, non attaquable). Toutes les redirections `returnUrl` des pages Identity scaffoldées utilisent déjà `LocalRedirect`. Rien à corriger.

## 15. XSS

Un seul `Html.Raw` dans toute l'application : `Views/Produits/ItemDetails.cshtml` sur `Produit.Description`. Le champ est édité côté admin via un `<input>`/`<textarea>` texte brut (le layout `_DashboardLayout.cshtml` des pages admin ne charge pas TinyMCE, contrairement à `_Layout.cshtml` qui n'est jamais utilisé pour ces pages) — jamais destiné à contenir du HTML riche. Remplacé par un rendu encodé standard (`@Model.Produit.Description`), supprimant le vecteur XSS stocké sans rien casser fonctionnellement. Tous les autres contenus utilisateur (avis clients, `Commentaire`) passent déjà par `Html.DisplayFor` (encodé par défaut).

## 16. Injection SQL

Recherche exhaustive de `FromSqlRaw`/`FromSqlInterpolated`/`ExecuteSqlRaw`/`ExecuteSqlInterpolated`/`SqlCommand` : zéro occurrence dans toute l'application. Toutes les requêtes passent par LINQ-to-Entities (paramétrage automatique par EF Core). Tri dynamique (`sortOrder`) implémenté via `switch` sur des expressions `OrderBy` codées en dur, jamais par concaténation de chaîne. Rien à corriger.

## 17. Tests de sécurité ajoutés

| Fichier | Tests | Couverture |
|---|---|---|
| `ImageUploadServiceTests.cs` | 15 | Upload : taille, extensions, MIME, signature, path traversal, unicité |
| `SecurityHeadersTests.cs` | 2 | CSP, nosniff, X-Frame-Options, Referrer-Policy (page normale + page d'erreur) |
| `RateLimitingTests.cs` | 2 | 429 au-delà de la limite, non-affectation des routes hors périmètre |
| `DatabaseOutageTests.cs` | 2 | Page d'erreur résiliente à une base injoignable (dev + production) |
| `AdminSurfaceAuthorizationTests.cs` | 3 | `Categories/Index` : anonyme refusé, client refusé, admin autorisé |

**Total nouveaux tests : 24.** Régression complète (SECURITY-001 + ECOM-CORE-001 + IDENTITY-COMMS-001 + SECURITY-002) : **116/116 PASS**.

## 18. Portes de qualité

```
BASELINE_SHA=9c2c037dbbcf241fa92cb887b0f7a8afbb9d0c2e
VULNERABLE_PACKAGES=0 (Critical=0, High=0, Moderate=0, Low=0)
STRIPESETTINGS_CS0436=0
BUILD=Release, 0 erreur
TESTS=116/116 PASS (92 hérités + 2 outage + 15 upload + 2 headers + 2 rate-limit + 3 admin-surface)
WARNING_DELTA=0 nouvelle occurrence nette (comparaison par occurrence normalisée fichier+code contre un worktree git au commit de base ; 8 occurrences résolues : CS0436 ×2 paires + CS8618 ×2 paires du fichier orphelin supprimé)
SECRET_SCAN=0 secret réel (working tree + historique git)
```

## 19. Hors scope confirmé

Aucune fonctionnalité e-commerce ajoutée. `SEARCH-001` non touché. Aucune migration de schéma. Un seul commit local scopé, non poussé.
