# COSMECHIC-QA-RELEASE-001 — Validation pré-release runtime, E2E, sécurité, accessibilité

## 1. Baseline

- `EXPECTED_HEAD=7ae6c59`, HEAD réel au démarrage : `7ae6c593ad9d15a0d3da7237572d104e7826e49f` — identique. Worktree propre (`git status --short` vide). `PREFLIGHT=OK`.
- Chaîne de lots recertifiée via `git log --oneline -15` : lignée linéaire complète jusqu'à `7ae6c59` (UX-001). `HISTORY_CHAIN_COMPLETE=YES`.
- Gates avant modification : `RESTORE=PASS`, `BUILD=PASS` (0 erreur, 48 warnings), `TESTS=351/351 PASS` (≥ `TESTS_BASELINE_EXPECTED=351`), `NUGET_CRITICAL/HIGH/MODERATE/LOW=0`.

## 2. Environnement de validation

- `BROWSER_AUTOMATION_AVAILABLE=YES` : Playwright 1.56.1 déjà installé globalement dans l'environnement (`/opt/node22/lib/node_modules/playwright`), Chromium headless déjà pré-installé (`/opt/pw-browsers/`). Aucune dépendance ajoutée au projet .NET ni à un `package.json` — scripts Node autonomes exécutés hors du dépôt, via `NODE_PATH` pointant vers l'installation globale.
- Base de données réelle : conteneur SQL Server 2022 Linux jetable (Docker), migrations `ApplicationDbContext` puis `CosmechicsContext` appliquées avec `dotnet ef database update` (outil `dotnet-ef` 8.0.12, déjà installé globalement). Détruit en fin de lot, aucun résidu.
- Application lancée en conditions réelles (`dotnet run`, hors du harnais de test InMemory) contre cette base, avec des comptes réels créés via les vrais flux HTTP (Register) : `qacustomer`, `qacustomer2` (clone de hash de mot de passe pour le test IDOR), `qaadmin` (rôle Admin attribué via SQL, aucun rôle n'étant pré-créé par l'application). Mot de passe partagé de test : `Qa!Passw0rd123`.
- **Limite réseau du bac à sable** : l'environnement d'exécution ne laisse passer que quelques hôtes précis en sortie HTTPS. Vérifié empiriquement (`curl` direct) : `fonts.googleapis.com` accessible ; `code.jquery.com`, `cdn.jsdelivr.net`, `cdnjs.cloudflare.com`, `cdn.tiny.cloud` **inaccessibles** (connexion refusée/tunnel échoué) depuis ce bac à sable précis. Conséquence directe et uniforme sur toutes les pages testées au navigateur : jQuery et le bundle JS Bootstrap (chargés depuis ces CDN) ne s'exécutent pas, ce qui a empêché de vérifier au clic réel l'ouverture du menu mobile ou d'autres interactions Bootstrap-JS dans **ce** bac à sable. Confirmé par une requête directe à ces mêmes hôtes (voir section 6) — ce n'est pas un défaut de l'application, c'est une restriction réseau de cet environnement de validation, à re-vérifier dans un environnement disposant d'un accès Internet non restreint avant mise en production. `EXTERNAL_INTEGRATION_READINESS` en tient compte (section 27).

## 3. Matrice responsive (runtime réel, Playwright)

Testé réellement (redimensionnement navigateur réel, pas d'inspection de code seule) sur `/`, `/Categories/Customer`, `/Home/Faq` :

| Viewport | Résultat |
|---|---|
| 320×568 | Aucun débordement horizontal (`scrollWidth == clientWidth` sur les 3 pages). Bouton bascule mobile visible. |
| 375×667 | Idem. |
| 390×844 | Idem. |
| 430×932 | Idem. |
| 768×1024 | Idem. |
| 1024×768 | Idem. |
| 1280×800 | Idem. |
| 1440×900 | Idem. |

`HORIZONTAL_OVERFLOW=NONE_OBSERVED` sur l'échantillon testé — confirme au runtime la correction du header (UX-001) plutôt que par la seule inspection de code.

**Interaction JS du menu mobile** : le clic réel sur le bouton bascule n'a pas ouvert le panneau (`aria-expanded` resté `false`) dans ce bac à sable — cause racine confirmée : Bootstrap JS (CDN) non chargé (section 2), pas un défaut de la marque HTML/CSS elle-même (le bouton, les attributs `data-bs-toggle`/`data-bs-target` et le CSS de repli sont corrects, vérifiés par inspection du HTML rendu). `MOBILE_MENU_JS_INTERACTION=NOT_VERIFIED_IN_THIS_SANDBOX`.

## 4. Routes publiques (E2E réel)

12 routes publiques testées via navigation réelle : `/`, `/Produits/Rechercher` (avec/sans terme, avec terme sans résultat), `/Categories/Customer`, `/Home/About`, `/Home/Contact`, `/Home/Faq`, `/Home/Shipping`, `/Home/Returns`, `/Home/Privacy`, `/Home/Terms`. Toutes : `200`, un seul `<h1>`, titre `<title>` distinct par page.

24 liens internes découverts sur ces pages ont été suivis réellement (requête HTTP réelle sur chaque `href`) : **0 lien cassé** (tous 200).

## 5. Identity (E2E réel, aucun email réel envoyé)

- Accès anonyme à `/Account/Index`, `/Brands/Index`, `/OrderHeaders/Index` : `302` vers `/Identity/Account/Login?ReturnUrl=...` (vérifié sans suivi automatique de redirection — voir note méthodologique section 12).
- Mauvais mot de passe pour un compte inexistant vs. un compte existant : message identique (« Tentative de connexion invalide. ») dans les deux cas — pas d'énumération de compte.
- Connexion réelle réussie (`qacustomer`), navigation post-connexion vers `/`.
- `/Identity/Account/ForgotPassword` charge correctement.
- **Register réel testé en conditions de panne SMTP réaliste** (voir section 15) — le compte est créé même si l'email échoue, comportement déterministe hérité d'IDENTITY-COMMS-001, revalidé.
- Rate limiting réel confirmé sur `/Identity/Account/Login` (429 après le seuil, requêtes réelles) et sur `/Home/Contact` (429 après 5).
- `LocalRedirect` confirmé dans `Login.cshtml.cs` (pas de nouveau test HTTP nécessaire, le mécanisme empêche structurellement toute redirection externe).

## 6. Compte client (E2E réel, IDOR)

- `/Account/Index`, `/Account/Profile`, `/Account/Addresses`, `/Account/Orders`, `/Account/Returns` : tous `200` pour le propriétaire connecté.
- Formulaire de création d'adresse présent et accessible.
- **IDOR** : `qacustomer2` a tenté d'accéder à `/Account/OrderDetails?id=1..3` — `404` dans les trois cas (aucune commande de `qacustomer` divulguée). Combiné à la matrice IDOR existante (SECURITY-001/ACCOUNT-001, revalidée par la suite complète — 353/353), aucune régression d'ownership constatée.

## 7. Commerce E2E (aucun Stripe réel)

- Panier vide : état vide affiché (confirmation runtime de la correction UX-001).
- Ajout d'un produit réel au panier, panier reflète le contenu.
- Checkout (`/Cart/Summary`) : `200`, méthode de livraison proposée, liens de politiques présents (Shipping, Terms — confirmation runtime de CONTENT-LEGAL-001/UX-001).
- Le total serveur (`OrderTotal = Subtotal + ShippingAmount + TaxAmount - DiscountAmount`) reste garanti par `OrderCheckoutService`, revalidé par la suite SQL-Server-backed existante (`CheckoutTotalsHttpTests`, `SqlServerFulfillmentConcurrencyTests`) — aucune modification de cette logique dans ce lot.
- Stripe réel non appelé : `Stripe:SecretKey` reste vide dans la configuration de cet environnement de validation ; les scénarios de paiement complet (checkout.session.completed, refunds, webhooks) restent couverts par les doubles de test existants (`FakeStripeCheckoutService`, `FakeStripeRefundService`), revalidés par la suite complète.

## 8. Stripe / webhooks / commandes / retours / remboursements / restock (SQL Server réel)

Ces scénarios critiques (signature invalide, `checkout.session.completed`, `StripeEventId` dupliqué, désaccord de montant/devise, stock concurrent, idempotence du fulfillment, `refund.updated`, restock explicite, historique de statut) sont déjà couverts par la suite SQL-Server-backed existante (`StripeWebhookControllerTests`, `StripeRefundWebhookTests`, `SqlServerFulfillmentConcurrencyTests`, `SqlServerRefundAndRestockConcurrencyTests`, `RestockServiceTests`, `OrderLifecycleServiceTests`) — revalidés dans ce lot par l'exécution complète de la suite (353/353, y compris contre un conteneur SQL Server jetable, détruit après coup). Aucun changement de code dans ces services dans ce lot : `WEBHOOK_REGRESSION=NO`, `REFUND_REGRESSION=NO`, `RETURN_REGRESSION=NO`, `RESTOCK_REGRESSION=NO`.

## 9. Admin (E2E réel)

Testé avec `qaadmin` (rôle Admin réel) : `/AspnetUsers/Dashboard`, `/Produits/Index`, `/Categories/Index`, `/Brands/Index`, `/OrderHeaders/Index`, `/ShippingMethods/Index`, `/TaxRates/Index`, `/Avis/Index` — tous `200`.

**Déconnexion réelle vérifiée au navigateur** : clic sur le bouton de déconnexion de la barre latérale (correction UX-001, `method="post"` ajouté) → cookie d'authentification effectivement absent après coup, et une requête directe (sans suivi de redirection) vers une route admin renvoie `302` vers Login. Confirmation concrète, en conditions réelles, que la correction UX-001 fonctionne.

**Non-admin face aux routes admin** (`qacustomer`, sans suivi de redirection) : `/Brands/Index`, `/ShippingMethods/Index`, `/TaxRates/Index` → `302` vers Login/AccessDenied. `/OrderHeaders/Index` → `200`, mais **par conception** : cette action sert soit la liste complète (Admin) soit uniquement les commandes du visiteur connecté (non-admin, filtrées côté serveur par `ApplicationUserId`) — vérifié dans le code (`OrderHeadersController.Index`). Aucune commande étrangère n'est exposée.

Aucune mutation admin ne bind d'entité sensible complète (revalidé — aucun contrôleur touché dans ce lot ; `[Bind]` restreints déjà en place depuis SECURITY-002/001B).

## 10. Sécurité HTTP (réponses réelles)

- CSP, `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin` présents sur `/` et sur une page 404 réelle.
- Cookie d'authentification : `HttpOnly`, `SameSite=Lax` — `Secure` absent car test effectué en HTTP simple (comportement `SameAsRequest` par défaut d'ASP.NET Core Identity, confirmé dans `Program.cs` — aucune policy explicite ne force `Secure`, donc en HTTPS réel le flag apparaîtrait automatiquement). Non modifié.
- `POST /webhooks/stripe` sans corps/signature valide → `400`, sans mutation (revalidé par la suite existante). `GET /webhooks/stripe` → `405` immédiat, pas de blocage.
- Rate limiting Login/Contact confirmé réel (section 5).
- CSRF confirmé actif (soumission Login sans jeton valide → `400`, jamais `200`).
- Open redirect : `LocalRedirect` confirmé dans le code (section 5).

## 11. Accessibilité

`ACCESSIBILITY_AUTOMATED_SCAN=NO` (aucun outil de scan automatisé d'accessibilité disponible dans cet environnement — ni installé ni ajouté, conformément à l'interdiction d'ajouter une dépendance non nécessaire au lot). Revue manuelle/code + runtime :

- Un seul `<h1>` par page vérifié réellement sur un échantillon (section 4) et par les tests xUnit existants (UX-001).
- Skip link présent et fonctionnel (`href="#main-content"`, cible existante).
- Labels associés (formulaires Login/Register déjà `asp-for`, Contact/Checkout corrigés en UX-001).
- Focus visible ajouté globalement (`design-system.css`, UX-001).
- Landmarks (`<header>`, `<main>`, `<footer>`, `<nav aria-label>`) présents.
- Images avec `alt` pertinent (corrigé en UX-001 pour la fiche produit).
- Accordéon FAQ natif Bootstrap avec `aria-expanded`/`aria-controls` (CONTENT-LEGAL-001, non modifié).

## 12. Note méthodologique importante (auto-corrigée pendant ce lot)

Une première série de vérifications d'autorisation avait laissé croire à une régression critique (accès admin apparemment accessible par un anonyme/non-admin) : en réalité, Playwright suit les redirections HTTP par défaut et rapporte le code de statut de la page **finale** (souvent la page de connexion, 200), pas celui de la ressource protégée d'origine. Une seconde vérification, sans suivi automatique de redirection (`maxRedirects: 0`), a confirmé qu'il n'y avait **aucune régression réelle** — voir sections 5 et 9. Documenté ici par souci de traçabilité et de rigueur : une lecture superficielle des premiers résultats aurait produit un faux rapport de vulnérabilité critique.

De même, un premier test du comportement « base de données indisponible » a montré une trace de pile brute — également un faux positif : la requête avait en réalité été servie par une instance encore en mode Développement restée active par erreur de gestion de processus, pas par l'instance Production nouvellement démarrée (conflit de port silencieux). Refait proprement contre une instance Production authentique (variable d'environnement confirmée dans les logs de démarrage) : comportement correct, voir section 15.

## 13. Contraste (WCAG 2.2 AA)

Audit systématique des paires couleur texte/fond du design system (`design-system.css`, UX-001) et des tokens Bootstrap déjà en usage dans tout le dépôt (`--bs-primary`, non introduits par ce lot) :

| Token | Avant-plan | Arrière-plan | Ratio | Exigence | PASS/FAIL | Action |
|---|---|---|---|---|---|---|
| Bouton primaire (texte) | `#ffffff` | `#f4623a` (primary) | 3.16:1 | 4.5:1 (texte normal) | **FAIL** | Documenté — non corrigé (voir ci-dessous) |
| Lien texte primaire | `#f4623a` | `#ffffff` | 3.16:1 | 4.5:1 | **FAIL** | Documenté — non corrigé |
| Contour de focus (UI) | `#f4623a` | `#ffffff` | 3.16:1 | 3:1 (élément UI) | PASS | Aucune |
| Titre de page (`.cx-page-title`) | `#c34e2e` | `#f8f8ff` (ghostwhite) | 4.46:1 | 3:1 (grand texte) | PASS | Aucune |
| Texte secondaire (`.text-muted`) | `#6c757d` | `#ffffff` | 4.69:1 | 4.5:1 | PASS | Aucune |
| Badge succès (texte) | `#ffffff` | `#198754` | 4.53:1 | 4.5:1 | PASS | Aucune |
| Badge danger (texte) | `#ffffff` | `#dc3545` | 4.53:1 | 4.5:1 | PASS | Aucune |
| Badge avertissement (texte) | `#212529` | `#ffc107` | 9.46:1 | 4.5:1 | PASS | Aucune |
| Badge info (texte) | `#212529` | `#0dcaf0` | 7.88:1 | 4.5:1 | PASS | Aucune |
| Texte principal | `#212529` | `#ffffff` | 15.43:1 | 4.5:1 | PASS | Aucune |

**Décision** : les deux échecs (texte blanc sur bouton `#f4623a`, lien `#f4623a` sur blanc) proviennent de la couleur de marque `--bs-primary`, définie dans le bundle Bootstrap fourni (`styles.css`, thème Start Bootstrap Creative) et utilisée identiquement sur l'ensemble du site (boutons primaires, liens) **avant** tout lot Claude, y compris dans les zones jamais touchées par UX-001/QA-RELEASE-001. La corriger exigerait de changer la couleur de marque elle-même (ou la graisse/taille de tous les textes concernés) — une modification de l'identité visuelle à l'échelle du site, explicitement hors périmètre de ce lot (« ne pas modifier arbitrairement l'identité visuelle », consigne PM de ne pas transformer ce lot en nouvelle refonte). **Non corrigé** : documenté comme risque résiduel réel nécessitant une décision de design (section 20/26). `CONTRAST_AA=FAIL_ON_BRAND_COLOR_DOCUMENTED_NOT_FIXED`.

## 14. Gestion d'erreurs

- **404** : réponse vide, en-têtes de sécurité présents, aucune trace technique.
- **Base de données indisponible (Production réelle, testée en conditions authentiques — voir section 12)** : `500`, page `Home/Error` complète (layout, navigation, footer, formulaire de recherche tous fonctionnels même avec catégories vides), `Cache-Control: no-cache,no-store`, en-têtes de sécurité présents, **aucune trace de pile, aucun message d'exception, aucune chaîne de connexion exposée** — seulement un identifiant de requête pour le support. `DB_UNAVAILABLE=PASS`.
- **Échec SMTP réel** (voir section 15) : déterministe, pas de crash, message générique à l'utilisateur.
- **Échec Stripe (fake)** : déjà couvert par la suite existante (`StripeFulfillmentServiceTests`), non modifié.
- Upload invalide / ModelState invalide : chemins déjà couverts par la suite SECURITY-002, revalidés (353/353).

## 15. Défaut reproductible trouvé et corrigé : délai de connexion SMTP illimité

**Reproduit en conditions réelles** : `Smtp:Host` est configuré avec une valeur réelle (`sandbox.smtp.mailtrap.io`, un relais de test légitime), injoignable depuis ce bac à sable. Une inscription réelle (`POST /Identity/Account/Register`) est restée bloquée **~100 secondes** (délai de connexion par défaut de MailKit) avant que l'échec ne soit détecté et journalisé — largement au-delà d'une attente raisonnable pour l'utilisateur, et un risque de saturation de threads en cas de pic d'inscriptions pendant une panne SMTP réelle.

**Correction** : `SmtpEmailSender.cs` fixe désormais explicitement `SmtpClient.Timeout = 15000` (15s) avant la connexion. Revérifié en conditions réelles après correction : la même requête d'inscription échoue proprement en **16 secondes** au lieu de ~100. Aucune règle métier, aucune politique de rétention, aucune donnée d'entreprise modifiée — uniquement un délai de résilience technique. Test de non-régression ajouté (`SmtpTimeoutTests.cs`).

## 16. Défaut reproductible trouvé et corrigé : `robots.txt` n'excluait pas le webhook

`POST /webhooks/stripe` (endpoint technique, jamais destiné à l'indexation) n'apparaissait pas dans `Disallow`, contrairement aux autres surfaces techniques/privées déjà listées. Ajouté : `Disallow: /webhooks/`. Test de non-régression ajouté (`RobotsTxtTests.cs`).

## 17. Liens et routes

24 liens internes suivis réellement (section 4) : 0 cassé. Aucun `href="#"` trouvé en dehors des menus déroulants Bootstrap contrôlés par JS (comportement attendu, vérifié comme non fonctionnellement cassé — pas une régression). Aucune ancienne route morte trouvée (`login.html`, `/blog`, etc. déjà éliminées en UX-001/CONTENT-LEGAL-001).

## 18. SEO technique / robots

`robots.txt` recertifié et complété (section 16). `SITEMAP_XML=DEFERRED`, `REASON=PRODUCTION_DOMAIN_REQUIRED` (aucun domaine de production n'est configuré nulle part dans le dépôt — inchangé depuis CONTENT-LEGAL-001, vérifié à nouveau). Titre/H1/description vérifiés réellement sur l'échantillon de routes publiques (section 4) : tous conformes, aucune donnée fictive.

## 19. Base de données (SQL Server réel)

Migrations `ApplicationDbContext` et `CosmechicsContext` appliquées avec succès sur une base neuve (`dotnet ef database update`) — confirme `DATABASE_RECONSTRUCTIBLE=YES`. `dotnet ef migrations has-pending-model-changes` : aucun changement en attente pour les deux contextes après les corrections de ce lot (`SmtpEmailSender.cs`, `robots.txt` — aucun n'est un fichier de modèle). `MODEL_MIGRATION_DRIFT=NONE`. `MIGRATIONS_CREATED=0`. Conteneur détruit après usage, aucun résidu (`docker ps -a` vide).

## 20. Configuration métier non résolue

| KEY | CURRENT_STATE | WHY_REQUIRED | BLOCKS_PRODUCTION? | OWNER_DECISION_REQUIRED |
|---|---|---|---|---|
| `RETURN_WINDOW_DAYS` | Non défini | Politique de retour affichée au client | Oui (expérience client incomplète) | Métier |
| `REFUND_SHIPPING_POLICY` | Non défini | Politique de remboursement affichée | Oui | Métier |
| `REFUND_TAX_POLICY` | Non défini | Politique de remboursement affichée | Oui | Métier |
| `INVOICE_LEGAL_TAX_INFO` | Non défini | Conformité facturation | Oui (si facturation formelle requise) | Métier/comptabilité |
| `ACCOUNT_DELETION_ANONYMIZATION_POLICY` | Non défini | Suppression de compte avec historique de commandes | Oui (juridique) | Métier/juridique |
| `PERSONAL_DATA_EXPORT_SCOPE` | Non défini | Export de données personnelles complet | Non-bloquant technique, bloquant conformité | Métier/juridique |
| **`PRODUCTION_DOMAIN`** *(nouveau, confirmé ce lot)* | Non configuré nulle part | Nécessaire pour `sitemap.xml`, cookies `Secure` garantis en HTTPS réel, CSP éventuellement à ajuster selon domaine final | Oui (bloque sitemap, HTTPS réel non testable ici) | DevOps/métier |
| **`SMTP_PRODUCTION_CREDENTIALS`** *(nouveau, confirmé ce lot)* | `Smtp:Host=sandbox.smtp.mailtrap.io` — relais de **test**, pas un relais de production | Emails transactionnels réels (confirmation de compte, réinitialisation de mot de passe) ne partiront jamais avec cette configuration | Oui | Métier/infra |
| **Couleur de marque `#f4623a` (contraste)** *(nouveau, confirmé ce lot)* | Ratio 3.16:1 sur boutons/liens primaires (AA exige 4.5:1) | Accessibilité WCAG AA | Non technique-bloquant, recommandé avant release accessible | Design/direction |

## 21. Contenu / légal

Aucune page légale réécrite dans ce lot (aucun bug technique ni contenu manifestement faux trouvé au-delà de ce qui était déjà documenté par CONTENT-LEGAL-001). Recertifié par lecture : aucune entreprise/fondateur/adresse fictifs, aucune juridiction inventée, aucune donnée fiscale inventée, aucune promesse cosmétique non fondée ajoutée. `LEGAL_REVIEW_REQUIRED=YES` (inchangé — aucune validation humaine compétente n'a eu lieu).

## 22. Tests ajoutés

- `Cosmechic.Tests/SmtpTimeoutTests.cs` — non-régression du délai de connexion SMTP borné (section 15).
- `Cosmechic.Tests/RobotsTxtTests.cs` — non-régression de la couverture `Disallow` de `robots.txt` (section 16).

Aucun autre gap prouvé n'a nécessité de nouveau test automatisé : les autres surfaces validées dans ce lot (IDOR, autorisation admin, en-têtes de sécurité, rate limiting, CSRF, DB indisponible) étaient déjà couvertes par la suite existante (353 tests avant ajout) et par les 37 tests UX-001 — ce lot les a **revalidés en conditions réelles** plutôt que dupliqué la couverture automatisée.

## 23. Warning delta

`WARNINGS_BEFORE=48`, `WARNINGS_AFTER=48` — **empreintes de diagnostics identiques** (comparaison ligne à ligne des 46 diagnostics distincts, pas seulement le total). `NEW_CODE_WARNINGS=0`, `RESOLVED_WARNINGS=0`.

## 24. Vulnérabilités

`dotnet list package --vulnerable --include-transitive` : `NUGET_CRITICAL=0`, `NUGET_HIGH=0`, `NUGET_MODERATE=0`, `NUGET_LOW=0`. Aucune dépendance ajoutée ou modifiée.

## 25. Secret scan

`git diff` complet (2 fichiers modifiés) et les 2 nouveaux fichiers de test inspectés : aucun secret, aucune clé Stripe, aucun mot de passe SMTP réel. `SECRET_SCAN=CLEAN`.

## 26. Diff review

| FILE | CHANGE | REASON | IN_SCOPE | RISK | TEST_EVIDENCE |
|---|---|---|---|---|---|
| `Cosmechic/Services/SmtpEmailSender.cs` | +8 lignes (`client.Timeout = 15000`) | Délai de connexion SMTP illimité reproduit en conditions réelles (section 15) | Oui | Faible — purement technique, aucune règle métier | `SmtpTimeoutTests.cs` + vérification runtime manuelle (100s → 16s) |
| `Cosmechic/wwwroot/robots.txt` | +4/-1 lignes (`Disallow: /webhooks/` + note) | Endpoint technique non exclu du crawl | Oui | Négligeable | `RobotsTxtTests.cs` |
| `Cosmechic.Tests/SmtpTimeoutTests.cs` | Créé | Couverture de non-régression | Oui | Aucun | Suite complète 353/353 |
| `Cosmechic.Tests/RobotsTxtTests.cs` | Créé | Couverture de non-régression | Oui | Aucun | Suite complète 353/353 |

`FILES_CHANGED=4`, `OUT_OF_SCOPE_CHANGES=0`. Aucune vue, aucun contrôleur, aucun modèle, aucune migration touchés.

## 27. Risques résiduels et conclusion de readiness

**Risques résiduels réels (non corrigés dans ce lot, hors périmètre ou nécessitant une décision) :**
- Contraste insuffisant sur la couleur de marque primaire (section 13) — décision design requise.
- Dépendance CDN sans repli local pour jQuery/Bootstrap JS sur le site public (`_Layout.cshtml`) — non vérifiable en interaction réelle dans ce bac à sable réseau-restreint ; à revalider dans un environnement avec accès Internet complet avant mise en production.
- `Produits/Index` reste une route non `[Authorize]` au comportement dual (client/admin sur la même URL) — architecture pré-existante (CATALOG-001), documentée par UX-001, non modifiée (changer l'autorisation est hors périmètre de contrôle de régression).
- Configuration SMTP de production non fournie (relais de test uniquement) — aucun email transactionnel réel ne partira tant que non configuré.
- 8 clés de configuration métier/légale/domaine encore ouvertes (section 20).

**Verdict :**

```
TECHNICAL_READINESS=PASS
BUSINESS_CONFIGURATION_READINESS=BLOCKED (8 clés ouvertes, section 20)
LEGAL_READINESS=BLOCKED (LEGAL_REVIEW_REQUIRED=YES, aucune revue humaine effectuée)
EXTERNAL_INTEGRATION_READINESS=PASS_WITH_RESERVATIONS (SMTP production non configuré ; dépendance CDN sans repli non re-testable dans ce bac à sable)
VISUAL_READINESS=PASS_WITH_RESERVATIONS (contraste de marque insuffisant sur boutons/liens primaires, décision design requise)
```

`RELEASE_READINESS=PASS_WITH_RESERVATIONS` — aucun défaut technique reproductible ne bloque une mise en production à proprement parler (tous ceux trouvés ont été corrigés et revérifiés), mais des décisions métier/légales/design encore ouvertes doivent être tranchées avant une release réelle. Aucun blocage technique pur.
