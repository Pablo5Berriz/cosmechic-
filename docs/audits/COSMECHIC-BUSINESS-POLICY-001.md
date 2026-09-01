# COSMECHIC-BUSINESS-POLICY-001 — Politiques métier approuvées

Implémente uniquement les politiques explicitement approuvées par le PM (section 2 de la
directive) et le domaine de production (section 9B). Aucune règle supplémentaire n'est
inventée ; les décisions encore ouvertes (légal, secrets, contraste) restent explicitement
non tranchées.

## 1. Préflight

```
BASELINE_EXPECTED=57be2f4
HEAD (avant modification)=57be2f426438bcd520b1cbcc94789bf57838975c
WORKTREE=CLEAN
RESTORE=PASS
BUILD=PASS (0 erreur, 48 warnings)
TESTS (avant)=356/356 PASS
NUGET vulnérabilités=0
```

**Docker** : indisponible via le script d'init système (`ulimit -Hn 524288` refusé —
`Operation not permitted`, limite matérielle de ce conteneur plafonnée à 20000). Contourné
en démarrant `dockerd` directement (bypasse l'appel `ulimit` du script d'init). Une fois
démarré, Docker fonctionne normalement — SQL Server réel jetable a donc pu être provisionné
comme l'exige la section 11 de la directive. `STATUS≠BLOCKED`.

## 2-9B. Règles exactes implémentées

### RETURN_WINDOW_DAYS = 30

- `CommercePolicy:ReturnWindowDays` = `30` dans `appsettings.json` (source centrale
  unique, déjà prévue depuis COSMECHIC-CONTENT-LEGAL-001).
- Branché dans `ReturnService.CanRequestReturnAsync` (seule source de vérité, aucune
  duplication ailleurs) via `IOptions<CommercePolicyOptions>`.
- Date de référence : `OrderHeader.DeliveredAt` si disponible, sinon `ShippedAt`.
- **Frontière explicite** : comparaison sur la **date calendaire** (`.Date`, sans l'heure)
  plutôt que sur `TimeSpan.TotalDays` brut. Le jour 30 complet reste éligible
  (`elapsedCalendarDays > 30` ne rejette qu'à partir du jour 31).
- `COSMETIC_OPENED_PRODUCT_RETURN_POLICY` : aucune règle codée sur l'état d'un produit
  cosmétique ouvert/utilisé — reste `AWAITING_LEGAL_REVIEW`.

**Défaut réel trouvé et corrigé pendant les tests** : la première implémentation comparait
`(DateTime.UtcNow - referenceDate).TotalDays > 30`. Le test de frontière
(`DeliveredExactly30DaysAgo_IsEligible_FrontierIsInclusive`) a échoué — une commande livrée
"il y a exactement 30 jours" devenait inéligible dès que quelques millisecondes
s'écoulaient pendant le traitement de la requête (dérive de précision sub-jour). Corrigé en
comparant les dates calendaires plutôt que les jours fractionnaires — comportement stable,
indépendant du temps d'exécution. Preuve : `ReturnWindowTests.cs` (29/30/31 jours, commande
non expédiée, autre propriétaire).

### REFUND_SHIPPING_POLICY = ORIGINAL_SHIPPING_REFUNDED_ONLY_IF_MERCHANT_FAULT

- Nouveau modèle fermé `Cosmechic.Services.RefundCause` (enum : `CustomerRemorse`,
  `MerchantFault`) — jamais une chaîne libre postée par le navigateur.
- `IRefundOrchestrationService.RequestReturnRefundAsync(returnRequestId, cause, ...)` :
  **aucun paramètre `amount`** — le montant est entièrement calculé côté serveur.
- `MerchantFault` : `ShippingAmount` du remboursement = `OrderHeader.ShippingAmount`
  (frais de livraison originaux), **une seule fois par commande** (vérifié en relisant les
  `Refunds` existants à chaque tentative — y compris sous concurrence, voir section 11).
- `CustomerRemorse` : `ShippingAmount` = 0, toujours.
- Ne dépasse jamais `OrderTotal - RefundedAmount` (même garde que le chemin manuel
  pré-existant, section 25 COMMERCE-OPERATIONS-001B).
- Interface admin : `Cosmechic/Views/OrderOperations/Details.cshtml`, un `<select>` à deux
  options fermées rendues par le serveur (jamais de champ montant ni de texte libre) —
  action `TriggerReturnRefund` (`OrderOperationsController`), n'accepte que
  `{ReturnRequestId, RefundCause, Reason}`.

### REFUND_TAX_POLICY = PROPORTIONAL_TO_REFUNDED_TAXABLE_ITEMS_USING_ORIGINAL_ORDER_SNAPSHOT

- Jamais `TaxRate` actif courant ni recalcul depuis une configuration future — uniquement
  `OrderHeader.TaxAmount` (snapshot historique déjà établi COMMERCE-OPERATIONS-001A).
- `MerchandiseAmount` du retour = Σ(`OrderDetail.Price` × `ReturnItem.Quantity`) sur les
  lignes réellement retournées.
- `TaxRefundAmount` proportionnel = `TaxAmount × (MerchandiseAmount / Subtotal)`, arrondi à
  2 décimales, `MidpointRounding.AwayFromZero` (convention déjà utilisée par
  `RefundOrchestrationService` pour la conversion en cents Stripe).
- **Plafond déterministe** : `TaxRefundAmount = Min(proportionnel, TaxAmount - taxe déjà
  remboursée sur cette commande)` — le dernier remboursement d'une série de retours
  partiels absorbe exactement l'écart d'arrondi, ne dépasse jamais `TaxAmount` original.
- Preuve (SQL Server réel) : `SqlServerReturnRefundPolicyTests
  .SuccessivePartialReturns_ShippingRefundedAtMostOnce_TaxNeverExceedsOriginal` — deux
  retours partiels (60$/40$ sur un sous-total de 100$, taxe 5$) donnent 3$ puis exactement
  2$ (5$ - 3$ déjà remboursés), jamais plus que 5$ au total.

### ACCOUNT_DELETION_ANONYMIZATION_POLICY = ANONYMIZE_PERSONAL_DATA_WHILE_RETAINING_REQUIRED_TRANSACTIONAL_RECORDS

- Nouveau service centralisé `AccountAnonymizationService` (`IAccountAnonymizationService`).
- **Jamais de hard-delete** d'un compte avec historique de commandes — comportement
  précédent (blocage pur) remplacé par une anonymisation réelle.
- **Aucune FK cassée** : la ligne `AspNetUsers` n'est jamais supprimée, seulement modifiée
  en place — tous les FK existants (`OrderHeader.ApplicationUserId` non-nullable,
  `ReturnRequest.ApplicationUserId`, `Refund.RequestedByUserId`,
  `OrderStatusHistory.ActorUserId`, `StockMovement.ActorUserId`) restent valides par
  construction.
- Ce qui est anonymisé (non réversible, non identifiant) : `Email`/`UserName` Identity
  (`anon-<userId>@anonymized.invalid`, TLD réservé RFC 2606), `PhoneNumber` Identity vidé,
  mot de passe remplacé par une valeur aléatoire inconnaissable, 2FA désactivé, logins
  externes retirés, verrouillage permanent (`LockoutEnd = DateTimeOffset.MaxValue`).
  `OrderHeader.Name`/`PhoneNumber`/`StreetAddress` remplacés par des valeurs génériques.
- Ce qui est **conservé** (obligations comptables, section 7 de la directive) :
  `OrderHeader.City`/`State`/`PostalCode`/`CountryCode` — granularité géographique
  nécessaire à l'audit de la taxe appliquée par juridiction (TPS/TVQ, COMMERCE-
  OPERATIONS-001A) ; tout l'historique transactionnel (commandes, retours, remboursements,
  `OrderStatusHistory`, `StockMovement`) intact.
- `CustomerAddress` (carnet d'adresses vivant, pas un enregistrement transactionnel,
  section 15/42 ACCOUNT-001) : supprimé entièrement.
- Reconnexion empêchée : verrouillage permanent + mot de passe détruit + `SecurityStamp`
  changé implicitement (invalide toute session active).
- `DATA_RETENTION_PERIODS` (durée légale de conservation avant suppression réelle
  éventuelle) : **non codé, non inventé** — `AWAITING_LEGAL_REVIEW`.
- Preuve (SQL Server réel, via le vrai pipeline DI `Program.cs`, `UserManager<IdentityUser>`
  complet) : `AccountAnonymizationSqlServerTests.cs` — commande préservée, FK intacte,
  adresses supprimées, email/mot de passe non réversibles, `IsLockedOutAsync=true`, ancien
  mot de passe refusé.

### PERSONAL_DATA_EXPORT_SCOPE

Étendu (`DownloadPersonalDataModel`) au-delà du scaffolding Identity par défaut :

| Inclus | Exclu (obligatoire) |
|---|---|
| Profil Identity (`[PersonalData]`), logins externes, clé 2FA | Mots de passe/secrets (jamais inclus, aucun changement) |
| `CustomerAddress` (propres adresses) | — |
| `OrderHeader`/`OrderDetails` (propres commandes) | `PaymentIntentId` (identifiant technique Stripe interne) |
| `ReturnRequest`/items | `AdminComment` (note interne, jamais exposée au client) |
| `Refund` (montants/cause/statut) | `StripeRefundId`, `IdempotencyKey`, `FailureCode` (internes) |
| — | `RowVersion` / jetons de concurrence |
| — | Avis/reviews (voir ci-dessous) |

**Avis/reviews délibérément exclus** : `TemoignagesClient` ne porte aucune FK vers
`AspNetUsers` (seulement un instantané texte libre `Nom`, découvert en lisant le modèle) —
aucun moyen fiable de déterminer "quels avis appartiennent à ce client" sans une
heuristique de correspondance par nom, qui risquerait d'exposer l'avis d'un autre
utilisateur portant le même nom affiché (fuite de type IDOR). Ajouter une vraie FK est un
changement de schéma/rétro-remplissage de données hors du périmètre approuvé de ce lot.

- Format : JSON structuré (sections nommées), pas un dictionnaire plat.
- IDOR testé explicitement : chaque requête filtre sur `userId` = utilisateur
  **authentifié courant** (jamais un paramètre fourni par le client) —
  `PersonalDataExportTests.cs` prouve que l'export de A ne contient jamais rien de B et
  vice versa, avec deux clients distincts réellement seedés.

### 9B. PRODUCTION_DOMAIN — décision PM approuvée

```
PRODUCTION_DOMAIN=https://cosmechic.ca
PRODUCTION_HOST=cosmechic.ca
CANONICAL_BASE_URL=https://cosmechic.ca
```

- Nouvelle option `Cosmechic.Services.ApplicationOptions.PublicBaseUrl`, liée depuis
  `Application:PublicBaseUrl` (`appsettings.json`) — seule source de vérité pour les URLs
  absolues qui ne peuvent pas être dérivées de la requête courante (contrairement aux
  callbacks Identity, qui restent dérivés dynamiquement, RELEASE-CONFIG-001 section 8).
- **sitemap.xml réel** : `HomeController.Sitemap()`, route conventionnelle
  `GET /sitemap.xml` (attribut `[HttpGet("sitemap.xml")]`), 10 URLs publiques
  statiques/institutionnelles + racines de catalogue (`/`, About, Contact, Faq, Privacy,
  Terms, Shipping, Returns, `Produits/Index`, `Categories/Customer`) — jamais de route
  privée/admin/webhook. Retourne 404 si `PublicBaseUrl` n'est pas configuré (développement/
  tests) plutôt que de fabriquer une URL `localhost`.
- **Balise canonical** : ajoutée dans `_Layout.cshtml`, `PublicBaseUrl` + chemin courant
  **sans chaîne de requête** (évite de canonicaliser des variantes filtrées/triées comme
  des pages distinctes). Absente si `PublicBaseUrl` non configuré.
- **robots.txt** : `Sitemap: https://cosmechic.ca/sitemap.xml` ajouté ; disallow list
  inchangée (déjà recertifiée QA-RELEASE-001).
- **WWW_STRATEGY=REDIRECT_TO_APEX** : middleware ajouté dans `Program.cs` — `Host ==
  "www.cosmechic.ca"` (comparaison littérale du nom d'hôte uniquement, sans effet sur
  localhost/dev/tests) → 301 permanent vers `https://cosmechic.ca` + chemin + requête.
  **Aucune configuration DNS/Cloudflare touchée** (hors périmètre, interdit par la
  directive) — c'est un filet de sécurité applicatif, pas la solution de production
  attendue (qui sera au niveau DNS/CDN).
- Tests : `SitemapAndCanonicalTests.cs` — sitemap servi au bon endpoint/content-type,
  jamais `localhost`/`127.0.0.1`/`http://` dans un `<loc>`, jamais `www.` comme origine,
  aucune route privée listée ; canonical jamais `http://`/`localhost`/`www.` ; redirection
  www→apex réelle testée (301 + `Location` exact), hôte normal non affecté.

```
SITEMAP_XML=RESOLVED (réel, servi à /sitemap.xml)
CANONICAL_URLS=RESOLVED (balise ajoutée dans _Layout.cshtml)
```

## 6. Invariants financiers

`Subtotal`/`ShippingAmount`/`TaxAmount`/`DiscountAmount`/`OrderTotal` ne sont **jamais**
modifiés par le nouveau code — seul `OrderHeader.RefundedAmount` (compteur séparé, déjà
établi COMMERCE-OPERATIONS-001B) évolue. Les remboursements restent représentés séparément
(`Refund.MerchandiseAmount`/`ShippingAmount`/`TaxAmount`, nouvelle décomposition — voir
section 12). Reconfirmé par assertion explicite dans
`SqlServerReturnRefundPolicyTests.MerchantFault_FullItemReturn_...` (Subtotal/
ShippingAmount/TaxAmount/OrderTotal identiques avant/après, contre SQL Server réel).

```
ORDER_FINANCIAL_SNAPSHOT_IMMUTABLE=YES
```

## 11. SQL Server réel — preuve d'exécution

```
SQL_SERVER_RUNTIME=REAL (SQL Server 2022 Linux, conteneur Docker jetable)
DATABASE_RECONSTRUCTIBLE=YES
```

- Provisionné manuellement (`docker run mcr.microsoft.com/mssql/server:2022-latest`),
  migrations appliquées dans l'ordre (`ApplicationDbContext` puis `CosmechicsContext`).
- Scénario historique explicitement reproduit : base migrée jusqu'à
  `AddCustomerAddresses` (juste avant ce lot), un `Refund` legacy inséré directement en SQL
  (`Amount=40`, aucune décomposition), puis la nouvelle migration appliquée — le
  rétro-remplissage (`UPDATE Refunds SET MerchandiseAmount = Amount WHERE ...`) a été
  vérifié en relisant la ligne après migration : `MerchandiseAmount=40, ShippingAmount=0,
  TaxAmount=0, Cause=NULL`, `CK_Refunds_Breakdown_Equals_Amount` satisfaite. Aucune perte de
  données.
- Base vide reconstruite avec succès (les deux jeux de migrations, de zéro).
- Suites de tests SQL Server réelles ajoutées : `SqlServerReturnRefundPolicyTests.cs` (6
  tests, dont un test de **concurrence réelle** — deux `RequestReturnRefundAsync`
  simultanés sur la même commande, preuve que la livraison n'est jamais réclamée deux fois
  et que le solde remboursable n'est jamais dépassé, via le même patron de retry
  optimiste RowVersion que RequestRefundAsync) et `AccountAnonymizationSqlServerTests.cs`
  (2 tests, FK réelles).
- Conteneur jetable entièrement démonté après usage (`docker rm -f`), confirmé par
  `docker ps -a` vide.

```
MODEL_MIGRATION_DRIFT=NONE (ApplicationDbContext et CosmechicsContext)
MIGRATIONS_CREATED=1 (AddBusinessPolicyRefundBreakdown — Refund.MerchandiseAmount/
  ShippingAmount/TaxAmount/Cause, 4 CHECK constraints, rétro-remplissage historique)
```

## 12. Migration — détail

`Cosmechic/Migrations/20260901224105_AddBusinessPolicyRefundBreakdown.cs` :
- `Up` : ajoute 4 colonnes nullable/à défaut 0 sur `Refunds` (additif, aucune colonne
  supprimée), **rétro-remplit** `MerchandiseAmount = Amount` pour toute ligne existante
  (nécessaire — sans cela, `CK_Refunds_Breakdown_Equals_Amount` échouerait contre toute
  base contenant déjà des remboursements, découvert et corrigé pendant la validation SQL
  Server réelle ci-dessus), puis ajoute les 4 CHECK CONSTRAINT.
- `Down` : supprime les 4 CHECK CONSTRAINT puis les 4 colonnes — symétrique, aucune perte
  de données côté colonnes historiques (`Amount`, `Status`, etc. inchangées).
- Aucune donnée existante perdue dans les deux sens.

## 13. Régressions obligatoires

Toutes les suites historiques (SECURITY, IDENTITY, CATALOG, COMMERCE-001A, COMMERCE-001B,
ACCOUNT, CONTENT-LEGAL, UX, QA-RELEASE, RELEASE-CONFIG) exécutées dans la régression
complète — voir section 14. Un seul ajustement de test a été nécessaire :
`ContentLegalPagesTests.ReturnsPage_WithoutConfiguredPolicy_...` asserait explicitement
qu'AUCUNE valeur de fenêtre de retour n'était configurée (`ReturnWindowDays=null`,
comportement CONTENT-LEGAL-001) — prémisse rendue caduque par l'approbation PM de ce lot
(`ReturnWindowDays=30`), pas une régression. Renommé et réécrit pour affirmer la vraie
politique approuvée (`30 jours` affiché, plus de repli "non défini") ; le repli lui-même
reste couvert par `ReturnServiceTests`/`ReturnWindowTests` qui passent `ReturnWindowDays=
null` explicitement au niveau service.

## 14. Gates finaux

```
RESTORE=PASS
BUILD=PASS (0 erreur)
WARNINGS_BEFORE=48 (46 diagnostics uniques)
WARNINGS_AFTER=48 (46 diagnostics uniques, identiques après suppression des numéros de ligne)
NEW_CODE_WARNINGS=0
TESTS_BEFORE=356
TESTS_AFTER=386
TESTS_PASS=386
TESTS_FAIL=0
NUGET_CRITICAL=0  NUGET_HIGH=0  NUGET_MODERATE=0  NUGET_LOW=0
SECRET_SCAN=CLEAN (grep insensible à la casse sur password/secret/apikey/clé privée dans
  le diff complet, faux positifs légitimes — CheckPasswordAsync, RemovePasswordAsync,
  etc. — exclus manuellement, aucune vraie correspondance)
TEST_ARTIFACTS=0
DOCKER_LEFTOVERS=0 (conteneur démonté, docker ps -a vide)
```

## 15. Documentation

Ce fichier. Rapport final transmis au PM séparément (format imposé par la directive).

## 10. Contraste — rappel, aucune modification

Candidats déjà calculés en COSMECHIC-RELEASE-CONFIG-001, non appliqués (aucune décision
prise ce lot non plus — hors périmètre explicite de ce lot) :

| Candidat | HEX | Contraste sur blanc |
|---|---|---|
| 1 | `#f23f0d` | 3.82:1 |
| 2 | `#d9380c` | 4.64:1 |
| 3 | `#cb350b` | 5.18:1 (recommandé) |
| 4 | `#c1320b` | 5.63:1 |

```
BRAND_CONTRAST_DECISION=AWAITING_PM_DECISION
```

## 9. Données légales toujours bloquées

```
INVOICE_LEGAL_TAX_INFO=AWAITING_LEGAL_REVIEW
COSMETIC_OPENED_PRODUCT_RETURN_POLICY=AWAITING_LEGAL_REVIEW
DATA_RETENTION_PERIODS=AWAITING_LEGAL_REVIEW
```
Rien n'a été inventé pour ces trois clés.

## 23. Diff review

| FILE | CHANGE | REASON | CONFIG_KEY |
|---|---|---|---|
| `Cosmechic/appsettings.json` | +9/-3 | RETURN_WINDOW_DAYS=30, RefundShippingPolicy/RefundTaxPolicy (documentaires), Application:PublicBaseUrl | RETURN_WINDOW_DAYS, PRODUCTION_DOMAIN |
| `Cosmechic/Services/ReturnService.cs` | +41 | Fenêtre de retour branchée, source unique | RETURN_WINDOW_DAYS |
| `Cosmechic/Services/RefundCause.cs` (nouveau) | +18 | Modèle fermé shipping refund | REFUND_SHIPPING_POLICY |
| `Cosmechic/Models/Refund.cs` | +16 | Décomposition Amount | REFUND_SHIPPING_POLICY, REFUND_TAX_POLICY |
| `Cosmechic/Models/CosmechicsContext.cs` | +12 | Colonnes + CHECK constraints | idem |
| `Cosmechic/Migrations/20260901224105_...` (nouveau) | +99 | Migration + rétro-remplissage | idem |
| `Cosmechic/Services/IRefundOrchestrationService.cs` | +10 | Nouvelle méthode | idem |
| `Cosmechic/Services/RefundOrchestrationService.cs` | +129 | Calcul serveur, jamais le navigateur | idem |
| `Cosmechic/Models/ViewModels/OrderOperationsInputs.cs` | +13 | DTO sans champ Amount | idem |
| `Cosmechic/Controllers/OrderOperationsController.cs` | +24 | Action dédiée | idem |
| `Cosmechic/Views/OrderOperations/Details.cshtml` | +28/-1 | UI `<select>` fermé | idem |
| `Cosmechic/Services/AccountAnonymizationService.cs` + `IAccountAnonymizationService.cs` (nouveaux) | +105 | Anonymisation centralisée | ACCOUNT_DELETION_ANONYMIZATION_POLICY |
| `Cosmechic/Areas/.../DeletePersonalData.cshtml(.cs)` | +105/-30 | Anonymise au lieu de bloquer | idem |
| `Cosmechic/Areas/.../DownloadPersonalData.cshtml.cs` | +159/-42 | Export étendu, IDOR-safe | PERSONAL_DATA_EXPORT_SCOPE |
| `Cosmechic/Services/ApplicationOptions.cs` (nouveau) | +14 | PublicBaseUrl | PRODUCTION_DOMAIN |
| `Cosmechic/Controllers/HomeController.cs` | +51 | sitemap.xml réel | idem |
| `Cosmechic/Views/Shared/_Layout.cshtml` | +13 | Balise canonical | idem |
| `Cosmechic/wwwroot/robots.txt` | +8/-6 | Référence sitemap réel | idem |
| `Cosmechic/Program.cs` | +21 | DI + redirection www→apex | idem |
| `Cosmechic.Tests/*` (5 nouveaux + 2 modifiés) | +~600 | Preuve de chaque politique | — |

```
OUT_OF_SCOPE_CHANGES=0
```
Aucun fichier non justifiable.
