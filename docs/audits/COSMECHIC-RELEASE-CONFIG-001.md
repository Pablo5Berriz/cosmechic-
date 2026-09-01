# COSMECHIC-RELEASE-CONFIG-001 — Configuration de release / closure

Lot de **configuration**, pas de développement fonctionnel. Objectif : transformer les
réserves ouvertes de COSMECHIC-QA-RELEASE-001 en configuration explicite et vérifiable,
sans jamais inventer de décision métier ou juridique à la place du PM.

## 1. Baseline

```
EXPECTED_HEAD=04a5bc4
HEAD (avant modification)=04a5bc44ca668d2b31945a2410ff0a2f25ccc15d
BRANCH=claude/cosmechic-full-audit-0up8zj
WORKTREE=CLEAN
```
Baseline conforme. Recertification avant modification (section 2 de la directive) :

```
RESTORE=PASS
BUILD=PASS (0 erreur)
WARNINGS=48
TESTS=353/353 PASS
NUGET_CRITICAL=0  NUGET_HIGH=0  NUGET_MODERATE=0  NUGET_LOW=0
ApplicationDbContext drift = NONE ("No changes have been made to the model since the last migration.")
CosmechicsContext drift = NONE (idem)
```

**Note d'environnement** : contrairement à la session QA-RELEASE-001, le démon Docker
n'est pas disponible dans cette session (`Cannot connect to the Docker daemon` — le
service ne démarre pas, `ulimit: error setting limit (Operation not permitted)`). Aucune
base SQL Server jetable réelle n'a donc pu être provisionnée dans ce lot. Playwright +
Chromium restent en revanche disponibles (mêmes binaires globaux qu'en QA-RELEASE-001).
Cette contrainte est documentée honnêtement plutôt que contournée : les vérifications
runtime de ce lot (section 6) utilisent un serveur Kestrel réel en environnement
Production, mais avec une chaîne de connexion SQL Server volontairement injoignable —
suffisant pour les pages testées (anonymes, sans dépendance base — voir section 6),
insuffisant pour un scénario nécessitant des données réelles.

## 2. Recertification — voir section 1 (exécutée avant toute modification, gates identiques)

## 3. Inventaire canonique des configurations ouvertes

| KEY | SOURCE_LOT | CURRENT_STATE | TECHNICAL_DEFAULT | BUSINESS_DECISION_REQUIRED | LEGAL_DECISION_REQUIRED | SECRET_REQUIRED | BLOCKS_PREPROD | BLOCKS_PRODUCTION |
|---|---|---|---|---|---|---|---|---|
| RETURN_WINDOW_DAYS | COMMERCE-OPERATIONS-001B | `CommercePolicy:ReturnWindowDays=null` en config, **non lu/non appliqué par ReturnService** (aucune fenêtre calendaire codée) | Aucun | OUI | non | non | non (comportement actuel documenté : porte technique = statut expédié/livré uniquement) | OUI |
| REFUND_SHIPPING_POLICY | COMMERCE-OPERATIONS-001B | `CommercePolicy:RefundShippingPolicy=""` ; `RefundOrchestrationService` prend un `amount` explicite fourni par l'appelant (admin), aucune règle de calcul intégrée | Aucun | OUI | non | non | non | OUI |
| REFUND_TAX_POLICY | COMMERCE-OPERATIONS-001B | `CommercePolicy:RefundTaxPolicy=""` ; idem, montant décidé par l'appelant, snapshot fiscal original (`OrderHeader.TaxAmount`) jamais recalculé automatiquement | Aucun | OUI | non | non | non | OUI |
| INVOICE_LEGAL_TAX_INFO | CONTENT-LEGAL-001 | `BusinessInformation:{LegalBusinessName,BusinessAddress,TaxRegistrationNumbers}=""` ; `Cart/Receipt.cshtml` affiche conditionnellement | Aucun | non | OUI | non | non | OUI |
| ACCOUNT_DELETION_ANONYMIZATION_POLICY | ACCOUNT-001 | Suppression bloquée si `OrderHeaders.Any(ApplicationUserId==user.Id)` — politique technique minimale = blocage permanent, pas d'anonymisation | Blocage permanent (déjà en place) | OUI (anonymisation vs blocage permanent vs rétention réglementaire) | OUI | non | non | OUI |
| PERSONAL_DATA_EXPORT_SCOPE | Identity (scaffolding par défaut) | `DownloadPersonalData.cshtml.cs` exporte uniquement les propriétés `[PersonalData]` d'`IdentityUser` + logins externes + clé 2FA — **aucune donnée CosmechicsContext** (adresses, commandes, retours, remboursements, avis) | Export Identity minimal (déjà en place) | non | OUI | non | non | OUI |
| PRODUCTION_DOMAIN | CONTENT-LEGAL-001 / QA-RELEASE-001 | Non configuré nulle part ; toutes les URLs (callbacks Identity, liens email) sont dérivées dynamiquement de `Request.Scheme`/`Host` par requête | Aucun (dérivation dynamique déjà correcte, voir section 8) | OUI (uniquement pour sitemap.xml/canonical) | non | non | non (fonctionnalités actuelles n'en dépendent pas) | OUI (pour sitemap.xml/SEO uniquement) |
| SMTP_PRODUCTION_CREDENTIALS | QA-RELEASE-001 | `Smtp:Host=sandbox.smtp.mailtrap.io` (relais de test Mailtrap réel, pas un relais de production), `Username`/`Password` vides | Aucun | non | non | OUI | non | OUI |
| BRAND_CONTRAST_DECISION | QA-RELEASE-001 | `#f4623a` sur blanc = 3,16:1 (échec WCAG AA texte normal, seuil 4,5:1) | Voir section 5 — candidats calculés, aucun appliqué | OUI (décision design) | non | non | non | non (accessibilité, pas un blocage fonctionnel — mais reste une réserve VISUAL_READINESS) |

Recherche exhaustive effectuée dans `docs/audits/`, `appsettings*.json`, `Program.cs`,
`Services/`, `Controllers/`, `Views/`, marqueurs `TODO`/`FIXME`/
`TODO_REQUIRES_BUSINESS_CONFIGURATION` (9 occurrences, toutes déjà couvertes ci-dessus) :
aucune clé supplémentaire non documentée trouvée. Cette liste n'est pas garantie
exhaustive au-delà de ce périmètre de recherche.

## 4. Principe : rien n'est inventé

Aucune valeur de `RETURN_WINDOW_DAYS`, `REFUND_SHIPPING_POLICY`, `REFUND_TAX_POLICY`,
`ACCOUNT_DELETION_ANONYMIZATION_POLICY`, `PERSONAL_DATA_EXPORT_SCOPE`,
`INVOICE_LEGAL_TAX_INFO` ou `PRODUCTION_DOMAIN` n'a été choisie ou codée dans ce lot.
Chacune reste `AWAITING_PM_DECISION` ou `AWAITING_LEGAL_REVIEW` (section 3). Seul
`BRAND_CONTRAST_DECISION` a des candidats **calculés à titre d'option**, non appliqués
(section 5).

## 5. Contraste de marque — candidats, aucun appliqué

`--bs-primary` / `--bs-link-color` / `--bs-btn-bg` = `#f4623a`, utilisé en (a) texte de
lien sur fond blanc, (b) fond de bouton avec texte blanc. Les deux cas donnent le même
ratio (paire de couleurs symétrique) : **3,16:1**, sous le seuil 4,5:1 (texte normal).

Méthode : même teinte/saturation (HSL h=0,036 s=0,894), seule la luminosité est réduite
— aucune autre propriété visuelle changée.

| HEX | CONTRAST_ON_WHITE | CONTRAST_ON_DARK (#1a1a1a) | WCAG_NORMAL_TEXT (4.5:1) | WCAG_LARGE_TEXT/UI (3:1) | VISUAL_DISTANCE_FROM_CURRENT | RECOMMENDATION |
|---|---|---|---|---|---|---|
| `#f4623a` (actuel) | 3.16 | 5.52 | ÉCHEC | RÉUSSITE | — | — |
| `#f23f0d` (L=0.50) | 3.82 | — | ÉCHEC | RÉUSSITE | Très proche (même teinte, légèrement plus saturé visuellement) | Insuffisant pour texte normal |
| `#d9380c` (L=0.45) | 4.64 | — | RÉUSSITE (marge faible) | RÉUSSITE | Proche — brun-orangé légèrement plus foncé | Candidat minimal viable |
| `#cb350b` (L=0.42) | 5.18 | — | RÉUSSITE | RÉUSSITE | Modérée | **Candidat recommandé** (marge de rendu/anti-aliasing) |
| `#c1320b` (L=0.40) | 5.63 | — | RÉUSSITE | RÉUSSITE | Modérée-perceptible | Alternative si marge supplémentaire désirée |

**Aucun de ces candidats n'est appliqué.** `BRAND_CONTRAST_DECISION=AWAITING_PM_DECISION`.
Changer la couleur de marque sur l'ensemble du site (boutons, liens, accents) est une
décision design qui dépasse le périmètre technique de ce lot, conformément à
l'instruction explicite de ne pas transformer ce lot en refonte visuelle.

## 6. CDN / dépendances frontend externes — CORRIGÉ ET TESTÉ

| RESOURCE | CURRENT_URL (avant) | PURPOSE | VERSION_PINNED | INTEGRITY_ATTRIBUTE | CROSSORIGIN | LOCAL_COPY_AVAILABLE | FAILURE_IMPACT | ACTION |
|---|---|---|---|---|---|---|---|---|
| jQuery | `code.jquery.com/jquery-3.6.0.min.js` | Requis par `site.js` (`$(function(){...})`) et jquery-validation | 3.6.0 | non | non | **OUI**, `wwwroot/lib/jquery/dist/jquery.min.js`, même version exacte | CRITIQUE (bloquait toute la validation cliente + tout script dépendant de `$`) | **Self-hosté ce lot** |
| Bootstrap JS bundle | `cdn.jsdelivr.net/npm/bootstrap@5.2.3/...` | `data-bs-toggle` (menu mobile, dropdowns), `bootstrap.ScrollSpy` | 5.2.3 | non | non | **OUI**, `wwwroot/lib/bootstrap/dist/js/bootstrap.bundle.min.js`, v5.1.0 (même version que le CSS Bootstrap déjà local, ligne 19 de `_Layout.cshtml`) | CRITIQUE (menu mobile et dropdowns totalement non-fonctionnels, sans erreur serveur visible) | **Self-hosté ce lot** (réconcilie aussi l'incohérence CSS 5.1.0 / JS 5.2.3 préexistante) |
| jquery-validation / jquery-validation-unobtrusive | — | Validation client des formulaires | 1.17.0 | non | non | Déjà local (aucun changement) | — | Aucune (déjà correct) |
| Font Awesome | `cdnjs.cloudflare.com/.../font-awesome/6.0.0-beta3` | Icônes décoratives | 6.0.0-beta3 | non | non | non | Cosmétique (icônes manquantes, pas de blocage fonctionnel) | Non touché — téléchargement d'une nouvelle dépendance non justifié |
| Google Fonts | `fonts.googleapis.com` | Typographie | — | non | non | non | Cosmétique (repli sur police système) | Non touché |
| Bootstrap Icons | `cdn.jsdelivr.net/npm/bootstrap-icons@1.5.0` | Icônes décoratives | 1.5.0 | non | non | non | Cosmétique | Non touché |
| flatpickr | `cdn.jsdelivr.net/npm/flatpickr` | Sélecteur de date | non épinglé (`@latest` implicite) | non | non | non | **Aucun** — `flatpickr(...)` n'est invoqué nulle part dans le code (résidu du thème, mort) | Non touché (suppression = decision hors périmètre) |
| TinyMCE | `cdn.tiny.cloud/.../tinymce/7/...` | Éditeur WYSIWYG admin (Diagrammes/Dashboard uniquement) | clé de compte tiers | non | `referrerpolicy=origin` | non | Dégradé (admin uniquement, pas de UI publique) | Non touché — hors cœur de l'UI publique |
| sb-forms-latest.js | `cdn.startbootstrap.com` | Amélioration AJAX du template pour un `#contactForm` qui **n'existe pas** dans ce projet (le vrai formulaire Contact est un POST MVC classique, `asp-action="Contact"`) | `latest` | non | non | non | **Aucun** — jamais réellement invoqué sur ce formulaire | Non touché (résidu mort, suppression hors périmètre) |

**Correctif appliqué** : `Cosmechic/Views/Shared/_Layout.cshtml` — jQuery et le bundle JS
Bootstrap chargent désormais `~/lib/jquery/dist/jquery.min.js` et
`~/lib/bootstrap/dist/js/bootstrap.bundle.min.js` (fichiers déjà présents dans le dépôt,
aucun téléchargement). `Cosmechic/Program.cs` inchangé pour cette section.

**Preuve d'exécution réelle** (Playwright + Chromium, réseau CDN déjà bloqué par la
politique sortante du bac à sable — condition équivalente à « réseau externe coupé ») :
application réelle lancée via Kestrel en environnement **Production**
(`ASPNETCORE_ENVIRONMENT=Production`, `--no-launch-profile`, chaîne SQL Server
volontairement injoignable), page `/Home/Terms` (page anonyme statique, aucune
dépendance base — `ShoppingCartViewComponent` court-circuite sans requête pour un
visiteur anonyme).

```
jqueryDefined=true
bootstrapDefined=true
Clic sur .navbar-toggler → #navbarSupportedContent obtient la classe "show" = true
Clic sur #categoriesDropdown → aria-expanded="true", .dropdown-menu obtient "show" = true
```

Erreurs console résiduelles observées (attendues, non bloquantes pour le cœur de l'UI) :
`tinymce is not defined`, `SimpleLightbox is not defined`, échecs réseau sur
fontawesome/googlefonts/bootstrap-icons/flatpickr/sb-forms — toutes cosmétiques ou sur du
code mort, conformément au tableau ci-dessus.

```
FRONTEND_EXTERNAL_CDN_REQUIRED_FOR_CORE_UI=NO
```

Test automatisé ajouté : `Cosmechic.Tests/FrontendSelfHostingTests.cs` (3 tests — voir
section 17).

## 7. SMTP production — déjà externalisable, aucun code requis

`SmtpSettings` est lié via `builder.Configuration.GetSection("Smtp")`.
`WebApplication.CreateBuilder` inclut par défaut `AddEnvironmentVariables()` : toute clé
`Smtp:*` est déjà substituable par une variable d'environnement `Smtp__*` sans aucun
changement de code. Le correctif de timeout QA-RELEASE-001 (`client.Timeout = 15000`)
reste intact et n'est pas touché par ce lot.

Variables nécessaires en exploitation (aucune valeur réelle fournie ici) :
```
Smtp__Host
Smtp__Port
Smtp__Username
Smtp__Password
Smtp__From ... (réellement : Smtp__FromAddress, Smtp__FromName — noms de propriétés réels de SmtpSettings)
```
En développement local, `UserSecretsId` est déjà configuré dans `Cosmechic.csproj`
(`aspnet-Cosmechic-db86447f-...`) — `dotnet user-secrets set "Smtp:Password" "..."`
fonctionne déjà sans modification.

```
SMTP_PRODUCTION_CREDENTIALS=AWAITING_SECRET_PROVISIONING
```
Aucun mot de passe réel créé ou committé. Aucun email réel envoyé dans ce lot.

## 8. Domaine de production / PublicBaseUrl

Audit de toutes les fonctionnalités à URL absolue :
- Callback de confirmation Identity (`Register.cshtml.cs`) : `Url.Page(..., protocol: Request.Scheme)` — dérivé dynamiquement de la requête HTTP courante, **pas** d'URL codée en dur.
- Reset password / confirmation email : même patron (`Request.Scheme` + `Host` implicite via `Url.Page`).
- robots.txt : relatif, aucune URL absolue.
- sitemap.xml : n'existe pas (`SITEMAP_XML=DEFERRED`, inchangé).
- canonical : aucune balise canonical n'existe actuellement dans le code.
- Stripe : aucune URL de redirection/callback construite dans ce dépôt (webhook = route relative `/webhooks/stripe`, appelée par Stripe vers une URL de production qui sera fournie lors de la configuration du endpoint côté dashboard Stripe — hors code).

**Décision retenue (RESOLVED_BY_CODE)** : ne **pas** introduire de classe
`ApplicationOptions.PublicBaseUrl` inutilisée. Le patron actuel — dérivation dynamique
via `Request.Scheme`/`Host` — est le patron ASP.NET Core recommandé, et devient correct
même derrière un futur reverse proxy TLS-terminating grâce au middleware
`ForwardedHeaders` ajouté section 18 (qui réécrit `Request.Scheme` à partir de
`X-Forwarded-Proto` quand la source est fiable). Introduire une configuration statique
non consommée par aucune fonctionnalité réelle serait de la sur-architecture — évitée
conformément à la section 15 de la directive.

La seule fonctionnalité qui aurait réellement besoin d'un domaine explicite
(`sitemap.xml`, balises `canonical`) reste différée :
```
PRODUCTION_DOMAIN=AWAITING_PM_DECISION
PUBLIC_BASE_URL_STRATEGY=RESOLVED_BY_CODE (dérivation dynamique par requête, cohérente avec ForwardedHeaders section 18)
```

## 9. RETURN_WINDOW_DAYS — audit, aucune valeur choisie

`ReturnService.CanRequestReturnAsync` (lu intégralement) : la seule porte actuelle est
`FulfillmentStatus ∈ {Shipped, Delivered}` + solde de quantité non déjà réclamée. **Aucun
calcul de jours écoulés depuis l'expédition/livraison n'existe dans le code** — une
commande livrée il y a un an resterait éligible aujourd'hui. `CommercePolicy:ReturnWindowDays`
existe en configuration (nullable) mais n'est lu par aucun service.

Options possibles (documentées, aucune choisie) : fenêtre fixe en jours depuis livraison ;
fenêtre différenciée par catégorie de produit ; pas de fenêtre (politique actuelle de
fait). Le code permet déjà une configuration centralisée (`CommercePolicyOptions`) sans
règle dispersée — il suffira de brancher la valeur une fois décidée.
```
RETURN_WINDOW_DAYS=AWAITING_PM_DECISION
```

## 10. REFUND_SHIPPING_POLICY — audit, aucune valeur choisie

`RefundOrchestrationService.RequestRefundAsync` prend un `amount` explicite fourni par
l'appelant (l'action admin qui déclenche le remboursement) ; le service ne calcule lui
même aucune part de frais de port. Impact des options :
- A. Frais de port jamais remboursés → l'admin exclut manuellement le montant du port du `amount` transmis (déjà possible techniquement).
- B. Remboursés uniquement si remboursement total → nécessiterait une vérification explicite (`amount == order.Total`) avant d'inclure `ShippingCost`, non implémentée.
- C. Décision au cas par cas par l'admin → comportement actuel de fait (aucune contrainte).

Aucune de ces politiques n'est implémentée ou choisie.
```
REFUND_SHIPPING_POLICY=AWAITING_PM_DECISION
```

## 11. REFUND_TAX_POLICY — audit, aucune valeur choisie

Le montant de taxe original est conservé en snapshot immuable sur `OrderHeader.TaxAmount`
(politique déjà établie en COMMERCE-OPERATIONS-001A : ne jamais recalculer une taxe
historique à partir des taux courants). `RefundOrchestrationService` ne touche jamais ce
snapshot ; le montant remboursé (taxe incluse ou non) reste entièrement à la discrétion
de l'appelant. Remboursement proportionnel vs total vs exclusion de la taxe : aucune règle
codée.
```
REFUND_TAX_POLICY=AWAITING_PM_DECISION
```

## 12. Données légales de facturation

`BusinessInformationOptions` existe déjà (COSMECHIC-CONTENT-LEGAL-001) avec
`LegalBusinessName`, `BusinessAddress`, `TaxRegistrationNumbers`, `SupportEmail` (déjà
rempli, `equipe.cosmechic@gmail.com`), `SupportPhone` — tous vides sauf `SupportEmail`.
Champs probablement nécessaires pour une facture légalement conforme (inventaire, aucune
supposition sur l'obligation juridique exacte) : nom légal de l'entreprise, adresse,
numéro(s) d'enregistrement fiscal (TPS/TVQ au Canada), coordonnées de contact,
informations vendeur. **Aucune de ces valeurs n'est inventée.**
```
INVOICE_LEGAL_TAX_INFO=AWAITING_LEGAL_REVIEW
LEGAL_REVIEW_REQUIRED=YES (inchangé depuis CONTENT-LEGAL-001)
```

## 13. Suppression / anonymisation de compte

Comportement actuel (`DeletePersonalDataModel`, lu intégralement) : suppression
**bloquée en permanence** si `OrderHeaders.Any(ApplicationUserId==user.Id)` — stratégie A
(blocage permanent) de fait. Comparaison des trois stratégies possibles :

| Stratégie | FK/historique | Obligations comptables | Retours/remboursements liés | Faisabilité technique |
|---|---|---|---|---|
| A. Blocage permanent (actuel) | Intact, aucun risque | Intact | Intact | Déjà implémenté |
| B. Anonymisation irréversible | Casse le nom/email affiché sur historique de commandes légitimes ; nécessite de décider quoi anonymiser (adresse, nom, email) sans casser `OrderHeader.ApplicationUserId` (FK non-nullable) | Risque si des identifiants nominatifs sont requis pour la comptabilité | Idem | Nécessite un nouveau service dédié, non trivial |
| C. Conservation réglementaire + anonymisation partielle | Le plus proche des obligations RGPD/PIPEDA typiques, mais la durée de rétention et les champs à anonymiser sont une décision juridique | — | — | Nécessite une politique de rétention explicite (durée) |

Aucune stratégie n'est implémentée au-delà de A (déjà en place avant ce lot).
```
ACCOUNT_DELETION_ANONYMIZATION_POLICY=AWAITING_PM_DECISION
```

## 14. Export de données personnelles

`DownloadPersonalDataModel` (lu intégralement) = scaffolding Identity par défaut,
**non modifié** depuis l'import initial du projet.

| ENTITY | PERSONAL_DATA_PRESENT | EXPORT_CURRENTLY_INCLUDED | SHOULD_BE_REVIEWED | LEGAL_DECISION_REQUIRED |
|---|---|---|---|---|
| IdentityUser (`[PersonalData]`) | Email, UserName, PhoneNumber | OUI | non | non |
| Logins externes / clé 2FA | Identifiants de provider, clé TOTP | OUI | non | non |
| CustomerAddress | Nom du destinataire, téléphone, adresse postale complète | **NON** | OUI | OUI |
| OrderHeaders / OrderDetails | Historique d'achat (produits, montants, dates) | **NON** | OUI | OUI |
| ReturnRequest / ReturnItem | Motifs de retour, commentaires client | **NON** | OUI | OUI |
| Refund | Historique de remboursement | **NON** | OUI | OUI |
| OrderStatusHistory | Horodatage des changements d'état de commande | **NON** | OUI | OUI |
| StockMovement | Aucune donnée personnelle directe (mouvements de stock) | non applicable | non | non |
| TemoignagesClient (avis) | Commentaires/notes attribués à l'utilisateur | **NON** | OUI | OUI |

L'export actuel ne couvre donc qu'une fraction des données personnelles réellement
détenues par l'application. Étendre l'export sans définition légale précise du périmètre
(quelles entités, quel format, quelle rétroactivité) serait inventer une portée — non
fait dans ce lot.
```
PERSONAL_DATA_EXPORT_SCOPE=AWAITING_PM_DECISION (portée) puis AWAITING_LEGAL_REVIEW (conformité)
```

## 15. Configuration centralisée

Déjà largement en place avant ce lot : `CommercePolicyOptions`, `BusinessInformationOptions`,
`SmtpSettings`, `StripeSettings`, `ImageUploadSettings` — patron Options .NET déjà utilisé
de façon cohérente. Aucune nouvelle classe de configuration inutile ajoutée dans ce lot
(voir section 8 — décision explicite de ne pas créer `ApplicationOptions.PublicBaseUrl`
sans consommateur réel, pour éviter la sur-architecture).

## 16. Validation de configuration — décision explicite de ne PAS ajouter de fail-fast

Un mécanisme de fail-fast au démarrage (lever une exception si `Stripe:SecretKey`,
`Smtp:Host` ou une valeur similaire est vide en Production) a été envisagé (section 16 de
la directive) puis **délibérément écarté** après lecture de
`Cosmechic.Tests/DatabaseOutageTests.cs` : `DatabaseOutageProductionTests` exerce
volontairement l'application en environnement **Production** avec les valeurs vides par
défaut d'`appsettings.json` (`Stripe:SecretKey=""`, `Smtp:Password=""`, etc.) pour prouver
que l'application dégrade proprement plutôt que de s'effondrer — comportement validé et
intentionnel depuis COSMECHIC-SECURITY-002. Ajouter un `throw` au démarrage sur ces mêmes
valeurs aurait cassé ce test historique et contredit un comportement déjà validé — une des
conditions d'arrêt explicites de la directive (« régression de test historique cassé »).
Le choix retenu est documentaire plutôt que coercitif : la checklist de préproduction
(section 17) liste les valeurs à vérifier manuellement/semi-automatiquement avant mise en
exploitation réelle, sans bloquer le démarrage de l'application dans les environnements où
ces valeurs sont légitimement absentes (développement, tests).

Aucune valeur de repli fictive n'a été ajoutée pour `PRODUCTION_DOMAIN`,
`SMTP_PRODUCTION_CREDENTIALS`, l'identité légale ou les numéros fiscaux — toutes ces clés
restent vides/nulles en configuration par défaut.

```
CONFIG_VALIDATION=RESOLVED_BY_EXISTING_DECISION (documentaire, pas de fail-fast — voir justification ci-dessus)
```

## 17. Checklist de préproduction (procédure documentée, semi-automatique)

À vérifier manuellement par l'opérateur avant toute mise en exploitation réelle :

| # | Vérification | Commande / méthode | Résultat attendu |
|---|---|---|---|
| 1 | Environnement | `echo $ASPNETCORE_ENVIRONMENT` | `Production` |
| 2 | Chaîne de connexion réelle | `ConnectionStrings__DefaultConnection` définie (env var ou secret store) | Pointe vers le vrai serveur SQL Server de production, jamais committée |
| 3 | Migrations appliquées | `dotnet ef database update --context ApplicationDbContext ...` puis `--context CosmechicsContext ...` | Sans erreur, dans cet ordre (FK dépendance) |
| 4 | Pas de drift | `dotnet ef migrations has-pending-model-changes --context <X> ...` (x2) | `No changes have been made to the model since the last migration.` |
| 5 | PublicBaseUrl / domaine | Vérifier manuellement les liens de confirmation Identity dans un email réel de test | Scheme = `https`, host = domaine réel (pas `localhost`) |
| 6 | SMTP présent | `Smtp__Host`, `Smtp__Username`, `Smtp__Password` définis (sans les afficher) | Non vides, relais de production (pas Mailtrap sandbox) |
| 7 | Stripe présent | `Stripe__SecretKey`, `Stripe__PublishableKey`, `Stripe__WebhookSecret` définis (sans les afficher) | Non vides, clés `sk_live_`/`pk_live_` si Stripe réel activé (hors périmètre de ce lot) |
| 8 | Cookies sécurisés | Requête HTTPS réelle → en-tête `Set-Cookie` contient `Secure` | Présent (dépend de `UseHttpsRedirection`+`UseHsts`, déjà en place) |
| 9 | Reverse proxy | Si un reverse proxy est utilisé : `ForwardedHeadersOptions.KnownProxies`/`KnownNetworks` explicitement configurés dans `Program.cs` (actuellement à leurs valeurs par défaut, safe mais non actif au-delà du loopback) | Configuré selon la topologie réelle |
| 10 | En-têtes de sécurité | `curl -I` sur une page réelle | CSP/X-Content-Type-Options/X-Frame-Options/Referrer-Policy présents (déjà vérifié QA-RELEASE-001) |
| 11 | Fichiers statiques | `wwwroot/lib/jquery`, `wwwroot/lib/bootstrap` présents et servis | 200, taille non nulle |
| 12 | Secret scan | `git log -p` / outil de scan sur le commit de release | CLEAN |

Cette checklist reste manuelle par choix explicite (section 16) — aucun mécanisme
automatique ne bloque le démarrage de l'application.
```
PREPRODUCTION_CHECKLIST=RESOLVED_BY_CODE (documentée ci-dessus)
```

## 18. Reverse proxy / HTTPS

Avant ce lot : `app.UseHttpsRedirection()` et `app.UseHsts()` (hors Development) étaient
déjà présents, mais **aucun middleware `ForwardedHeaders` n'était enregistré** — derrière
un futur reverse proxy TLS-terminating, `Request.Scheme` resterait `http` (le proxy parle
HTTP au backend), cassant silencieusement les liens de confirmation Identity
(`Url.Page(..., protocol: Request.Scheme)`, vérifié section 8) et le rate limiting par IP
(`httpContext.Connection.RemoteIpAddress` verrait l'IP du proxy pour tous les clients,
regroupant tout le trafic dans une seule fenêtre de rate limit).

**Correctif appliqué** : `app.UseForwardedHeaders(...)` ajouté dans `Program.cs`, avant
`UseHttpsRedirection()`. `KnownProxies`/`KnownNetworks` **volontairement laissés aux
valeurs par défaut d'ASP.NET Core** (qui ne font confiance aux en-têtes
`X-Forwarded-For`/`X-Forwarded-Proto` que si la connexion directe provient du loopback) —
aucune IP ou réseau de production n'est inventée. Sans reverse proxy réel (le cas actuel),
ce middleware est un no-op complet, confirmé par les 356/356 tests toujours verts.

```
FORWARDED_HEADERS=RESOLVED_BY_CODE (scaffoldé, safe par défaut) / AWAITING_INFRA_CONFIGURATION (activation réelle nécessite KnownProxies/KnownNetworks une fois la topologie connue)
HTTPS_READINESS=PASS (UseHttpsRedirection + UseHsts déjà en place, inchangés)
SECURE_COOKIE_READINESS=PASS (SameAsRequest — devient Secure une fois tout le trafic HTTPS, déjà vérifié QA-RELEASE-001)
RATE_LIMIT_CLIENT_IP_READINESS=PASS_WITH_RESERVATIONS (correct dès que KnownProxies/KnownNetworks seront configurés pour la vraie topologie ; correct déjà sans reverse proxy)
```

## 19. Configuration Stripe

`Stripe:SecretKey=""`, `Stripe:WebhookSecret=""` (jamais committés avec une vraie valeur,
vérifié `git log` + contenu actuel). `Stripe:PublishableKey` contient une vraie clé
`pk_test_...` — **intentionnellement publique** (les clés publishable Stripe sont conçues
pour être exposées côté client, ce n'est pas un secret). Externalisable via
`Stripe__SecretKey`/`Stripe__WebhookSecret`/`Stripe__PublishableKey` (même mécanisme que
section 7). Comportement actuel en l'absence de `SecretKey` : le SDK Stripe échoue à la
première tentative d'appel réel (pas de fail-fast au démarrage — cohérent avec la
décision section 16). `REAL_STRIPE_USED=NO` dans ce lot, aucun appel Stripe réel effectué.

```
STRIPE_PRODUCTION_CONFIG=AWAITING_SECRET_PROVISIONING (SecretKey/WebhookSecret réels) — mécanisme d'externalisation déjà prêt (RESOLVED_BY_CODE)
```

## 20. Robots / Sitemap

`robots.txt` inchangé, toujours correct (recertifié QA-RELEASE-001, `/webhooks/` déjà
présent). Aucun domaine officiel fourni dans cette session.
```
SITEMAP_XML=DEFERRED, REASON=PRODUCTION_DOMAIN_REQUIRED (inchangé)
```

## 21. Tests

Un seul fichier de test ajouté, strictement pour la modification réellement faite (self-
hosting CDN, section 6) : `Cosmechic.Tests/FrontendSelfHostingTests.cs` (3 tests — voir
liste ci-dessous). Aucun test ajouté pour le middleware `ForwardedHeaders` : son
comportement par défaut est un no-op sans reverse proxy, déjà couvert implicitement par
le fait que les 353 tests historiques + les 3 nouveaux restent verts (aucune régression
introduite) ; écrire un test dédié aurait nécessité de simuler une topologie de proxy
inventée, contraire au principe de la section 4. Aucun test artificiel ajouté pour des
décisions encore ouvertes (return window, refund policy, etc.) — rien à tester tant
qu'aucun code n'implémente ces règles.

- `Layout_ReferencesLocalJQueryAndBootstrapBundle_NotCdn`
- `SelfHostedScript_IsServedSuccessfully("/lib/jquery/dist/jquery.min.js")`
- `SelfHostedScript_IsServedSuccessfully("/lib/bootstrap/dist/js/bootstrap.bundle.min.js")`

```
TESTS_BEFORE=353
TESTS_AFTER=356
TESTS_PASS=356
TESTS_FAIL=0
```

## 22. Gates finaux (après modifications)

```
RESTORE=PASS
BUILD=PASS (0 erreur)
WARNINGS_BEFORE=48
WARNINGS_AFTER=48
NEW_CODE_WARNINGS=0 (aucun des 48 warnings ne provient de Program.cs, _Layout.cshtml ou
                     FrontendSelfHostingTests.cs — vérifié par grep sur la liste complète
                     des diagnostics, pas seulement le total)
TESTS=356/356 PASS
NUGET_CRITICAL=0  NUGET_HIGH=0  NUGET_MODERATE=0  NUGET_LOW=0
ApplicationDbContext drift = NONE
CosmechicsContext drift = NONE
SECRET_SCAN=CLEAN (grep insensible à la casse sur password/secret/apikey/token dans le
                   diff complet — aucune correspondance)
TEST_ARTIFACTS=0 (git status --short ne montre que les fichiers listés section 23)
DOCKER_LEFTOVERS=NON_APPLICABLE_THIS_SESSION (Docker indisponible dans cette session dès
                  le départ — aucun conteneur n'a donc pu être laissé derrière ; voir
                  section 1)
STRAY_PROCESSES=0 (aucun processus dotnet/Cosmechic.dll résiduel après la vérification
                   runtime de la section 6, confirmé par ps aux)
```

## 23. Diff review

| FILE | CHANGE | REASON | CONFIG_KEY | IN_SCOPE | RISK | TEST_EVIDENCE |
|---|---|---|---|---|---|---|
| `Cosmechic/Program.cs` | +17/-0 | Ajout `UseForwardedHeaders` (section 18), safe par défaut (loopback uniquement) | `FORWARDED_HEADERS` | OUI | Faible (no-op sans reverse proxy, prouvé par régression) | 356/356 tests verts, y compris `DatabaseOutageProductionTests` |
| `Cosmechic/Views/Shared/_Layout.cshtml` | +10/-2 | Self-hosting jQuery + Bootstrap JS bundle (section 6), fichiers déjà présents dans le dépôt | `EXTERNAL_CDN_DEPENDENCY` | OUI | Faible (versions identiques/cohérentes, prouvé par Playwright réel) | Playwright réel (section 6) + `FrontendSelfHostingTests.cs` |
| `Cosmechic.Tests/FrontendSelfHostingTests.cs` (nouveau) | +42 | Preuve automatisée du correctif ci-dessus | — | OUI | Aucun (fichier de test) | S'exécute lui-même, 3/3 PASS |

```
OUT_OF_SCOPE_CHANGES=0
```
Aucun fichier non justifiable — pas de STOP requis à ce stade.

## 24-27. Documentation, verdict, commit

Voir sections précédentes pour le détail. Verdict et commit : voir rapport final transmis
au PM (hors de ce document, format imposé par la directive).

## 28. Points de vigilance résiduels (transmis explicitement, rien caché)

1. **Docker indisponible dans cette session** — contrairement à QA-RELEASE-001, aucune
   validation contre une vraie base SQL Server n'a été possible ici. Les changements de
   ce lot ne touchent ni les DbContexts ni aucune requête SQL, donc le risque réel est
   jugé faible, mais ce n'est pas une preuve d'exécution contre une base réelle comme
   l'exige la méthodologie de QA-RELEASE-001 pour les scénarios qui en dépendraient.
2. **9 décisions métier/juridiques restent ouvertes** (section 3) — aucune n'a été
   tranchée ni contournée par une valeur par défaut fabriquée.
3. **Contraste de marque** : candidats calculés, aucun appliqué — décision design en
   attente.
4. **ForwardedHeaders** : scaffoldé de façon sûre mais pas encore utile tant que
   `KnownProxies`/`KnownNetworks` n'auront pas été configurés pour la vraie topologie de
   production.
