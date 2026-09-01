# COSMECHIC-COMMERCE-OPERATIONS-001A — Livraison, taxation et intégrité du total de commande

- **Lot** : COSMECHIC-COMMERCE-OPERATIONS-001A
- **Base de départ** : `1e2f460` (COSMECHIC-CATALOG-001, PASS implicite)
- **Portée** : adresse de livraison/facturation, méthodes de livraison configurables, seuil de livraison gratuite, taxes (TPS/TVQ), sous-total, rabais existants (Promotion), total final, snapshot financier de commande, cohérence Stripe, affichage checkout, confirmation/reçu.
- **Hors scope, volontairement non touché** : retours, remboursements Stripe, annulations, factures/reçus PDF, API transporteurs, suivi d'expédition, coupons/promotions avancées, wishlist, newsletter, pages légales, SEO complet, A11Y complète, refonte générale, analytics, DevOps. Réservé à COSMECHIC-COMMERCE-OPERATIONS-001B.

## 0. Recertification technique

```
LOCAL_HEAD=1e2f4605e2ae4d5dd5b0aa3b0aabc3d96232cc72
WORKTREE=CLEAN (après nettoyage d'artefacts de test wwwroot/Images_Produits, non liés à ce lot)
RESTORE=PASS, BUILD=PASS (0 erreur), TESTS=136/136 PASS (baseline CATALOG-001)
```

## 1. Découverte fondatrice : incohérence pré-existante checkout affiché vs. montant facturé

Avant ce lot, `Cosmechic/Views/Cart/Summary.cshtml` affichait déjà au client un total incluant :
- TPS 5 % (`TPS_RATE = 0.05M`)
- TVQ 9,975 % (`TVQ_RATE = 0.09975M`)
- Livraison forfaitaire 15,00 $ (`SHIPPING_COST = 15.0M`)

… entièrement en dur, **côté vue uniquement**, jamais persistées ni transmises au serveur. `OrderCheckoutService.CreateCheckoutSessionAsync` calculait un `orderTotal` strictement égal au sous-total du panier (`cartItems.Sum(item => item.Produit.Prix * item.Count)`) et c'est ce montant, sans livraison ni taxe, qui était réellement soumis à Stripe. **Le client voyait donc un total différent de celui réellement facturé.** Ces valeurs ($15 livraison, 5 % TPS, 9,975 % TVQ) sont traitées comme des décisions commerciales déjà existantes (pas inventées) et migrées vers le nouveau modèle configurable (§5).

## 2. Modèle de données

### 2.1 `OrderHeader` (nouveaux champs)

```
Subtotal        money   NOT NULL, CHECK >= 0
ShippingAmount  money   NOT NULL, CHECK >= 0
TaxAmount       money   NOT NULL, CHECK >= 0
DiscountAmount  money   NOT NULL, CHECK >= 0
ShippingMethodId    int NULL, FK -> ShippingMethods (Restrict)
ShippingMethodName  nvarchar(200) NULL   -- snapshot, survit à un renommage/désactivation ultérieurs
OrderTotal (existant, inchangé)
```

Invariant imposé **au niveau moteur** (pas seulement en C#) :

```sql
CONSTRAINT CK_OrderHeaders_Total_Equals_Components
  CHECK ([OrderTotal] = [Subtotal] + [ShippingAmount] + [TaxAmount] - [DiscountAmount])
```

Vérifié contre un vrai SQL Server jetable : un `UPDATE` violant l'invariant est rejeté (`SqlServerConstraintTests.OrderHeader_InconsistentTotal_IsRejectedByCheckConstraint`), une ligne cohérente est acceptée.

### 2.2 `ShippingMethod` (nouvelle table)

```
ShippingMethodId, Name, Description, Price (money, >=0),
FreeShippingThreshold (money NULL, >=0 si non NULL), IsActive, EstimatedMinDays/MaxDays, SortOrder
```

Désactivation uniquement (jamais de suppression physique) : `FK_OrderHeaders_ShippingMethods` en `Restrict`. Le client envoie uniquement `ShippingMethodId` — jamais un montant. `IShippingCalculator`/`ShippingCalculator` chargent le prix depuis la base et rejettent toute méthode inexistante ou désactivée.

### 2.3 `TaxRate` (nouvelle table)

```
TaxRateId, Jurisdiction, CountryCode (2), RegionCode (NULL = toutes régions du pays),
Rate (decimal(9,6) — PAS money, c'est un taux pas un montant), EffectiveFrom, EffectiveTo (NULL), IsActive
```

Justification d'un modèle base de données plutôt qu'une simple valeur scalaire de configuration : le Québec exige la somme de **deux** taux actifs simultanés pour une même commande (TPS fédérale + TVQ provinciale) ; un scalaire unique ne peut pas représenter cela. `ITaxCalculator`/`TaxCalculator` somment toutes les lignes actives dont la fenêtre `EffectiveFrom..EffectiveTo` couvre l'instant présent et dont `RegionCode` est soit `NULL` (s'applique à tout le pays) soit égal à la région résolue. Aucun taux ne correspond → 0 $ de taxe, jamais une exception (`TODO_REQUIRES_BUSINESS_CONFIGURATION` implicite tant qu'une juridiction n'est pas configurée).

### 2.4 Migration `AddShippingAndTaxOrderTotals`

Stratégie de migration historique (section 45/46 du mandat) : les 4 nouvelles colonnes sont ajoutées avec `defaultValue = 0`, puis, **avant** l'ajout de `CK_OrderHeaders_Total_Equals_Components`, un `UPDATE [OrderHeaders] SET [Subtotal] = [OrderTotal]` reconstitue le sous-total historique à partir du seul total déjà connu (Shipping/Tax/Discount restent à 0 — aucune ventilation rétroactive n'est inventée). `OrderTotal` lui-même n'est jamais modifié : aucune commande historique ne change de montant facturé.

Validé contre un vrai SQL Server jetable (Docker) : une commande historique insérée avec l'ancien schéma (`OrderTotal = 123.45`, aucune des 4 nouvelles colonnes) survit à la migration avec `Subtotal = 123.45`, `Shipping/Tax/Discount = 0`, sans violation de contrainte CHECK. `dotnet ef migrations has-pending-model-changes` confirme `NO DRIFT` pour `ApplicationDbContext` et `CosmechicsContext`.

## 3. Calcul serveur — `OrderCheckoutService`

```
SERVEUR : Subtotal (panier, prix courants)
        + ShippingAmount (IShippingCalculator, jamais fourni par le client)
        + TaxAmount (ITaxCalculator, jamais fourni par le client)
        - DiscountAmount (0 ce lot, voir §6)
        = OrderTotal
        → Stripe Checkout (LineItems produits + ligne Livraison + ligne Taxes = OrderTotal exact)
```

`ShippingAddress` (DTO d'entrée) transporte désormais `ShippingMethodId` en plus des champs d'adresse — **toujours aucune valeur financière**. Le client ne peut physiquement pas influencer `Subtotal`/`ShippingAmount`/`TaxAmount`/`OrderTotal` : ces champs n'existent nulle part dans les types que MVC lie depuis le formulaire (`CheckoutFormInput`).

**Politique de taxation** :
- `TAX_ROUNDING_POLICY=PER_JURISDICTION_LINE_AWAY_FROM_ZERO` — chaque ligne de taxe (ex. TPS, TVQ) est arrondie séparément à 2 décimales (`MidpointRounding.AwayFromZero`) avant d'être additionnée aux autres lignes, jamais un arrondi unique sur la somme brute.
- Taxe calculée sur le **sous-total uniquement**, jamais sur les frais de livraison (cohérent avec la convention pré-existante de la vue).
- `SUPPORTED_CURRENCY=CAD` (`CheckoutConstants.Currency`, inchangé).

**Résolution de la région fiscale** : `RegionCodeResolver.ResolveCanadianRegionCode` — normalisation minimale et sûre (Trim, suppression des accents via NFD comme `SlugGenerator`, comparaison insensible à la casse). Seul le Québec est explicitement reconnu (seule juridiction déjà établie dans l'application avant ce lot, via TVQ) ; tout autre texte à 2 lettres est traité comme un code déjà normalisé, sinon aucune région n'est retenue (`RegionCode IS NULL` continue de s'appliquer pour les taux fédéraux). Aucune taxonomie provinciale complète n'est inventée.

## 4. Stripe

Les frais de livraison et les taxes sont ajoutés comme **lignes Stripe explicites** (`SessionLineItemOptions` supplémentaires), plutôt que via les paramètres `shipping_options`/`automatic_tax` de Stripe : `Session.AmountTotal` égale ainsi toujours exactement `OrderHeader.OrderTotal` (en cents), sans dupliquer le calcul fiscal côté Stripe. `Metadata` reste minimal (`OrderId` uniquement, inchangé).

`StripeFulfillmentService`/`StripeWebhookController` (COSMECHIC-ECOM-CORE-001) **n'ont nécessité aucune modification** : la validation `session.AmountTotal != (long)Math.Round(orderHeader.OrderTotal * 100, ...)` existait déjà et couvre automatiquement la nouvelle composition du total, puisque `OrderTotal` reste l'unique valeur validée. Les deux barrières d'idempotence (contrainte UNIQUE + `PaymentStatus == Approved`) restent inchangées et actives.

## 5. Checkout UI (`Cart/Summary`)

- **GET** : `CheckoutSummaryVM` — sous-total réel, méthodes de livraison actives (radio, prix affiché par option), taux de taxe actifs (pour aperçu). Les constantes en dur (`TPS_RATE`, `TVQ_RATE`, `SHIPPING_COST`) sont supprimées.
- **Aperçu client** : un petit script (nonce CSP) recalcule Livraison/Taxes/Total à l'affichage et à chaque changement de méthode/province, à partir des données serveur embarquées (prix des méthodes, taux actifs) — **jamais envoyé au serveur**, purement cosmétique. Le total réellement facturé est toujours recalculé server-side à la soumission, indépendamment de cet aperçu.
- **POST** : lie `CheckoutFormInput` (Name/PhoneNumber/StreetAddress/City/State/PostalCode/ShippingMethodId) — un type dédié sans aucune propriété financière ou d'état, remplaçant la liaison précédente sur `ShoppingCartVM.OrderHeader` complet. Un POST contenant des clés `OrderTotal`/`TaxAmount`/`ShippingAmount`/`PaymentStatus`/`ApplicationUserId`/`SessionId` n'a **littéralement aucune propriété où se lier** — prouvé par `CheckoutTotalsHttpTests.TamperedFinancialAndStateFields_AreIgnored_OrderUsesServerComputedTotal`.
- Méthode de livraison invalide ou désactivée → échec propre (`TempData["error"]`), jamais de commande créée, jamais d'appel Stripe.
- Panier vide → déjà rejeté par `OrderCheckoutService` (comportement pré-existant, inchangé).

`OrderConfirmation.cshtml` et `OrderHeaders/Details.cshtml` (admin + client) affichent désormais Produits/Sous-total/Livraison (avec nom de méthode)/Taxes/Rabais (si > 0)/Total — toujours depuis le **snapshot persisté**, jamais recalculé depuis les prix courants des produits ou l'état actuel des `ShippingMethod`/`TaxRate`.

## 6. Rabais / `Promotion`

`Promotion` (`Id, Titre, Description, Remise, DateDebut, DateFin`) audité : aucune FK vers `Produit` ou `OrderHeader`, c'est une bannière marketing d'accueil horodatée, non connectée au panier ni au checkout. Aucun moteur de coupon n'est construit ce lot (hors scope explicite). `DiscountAmount = 0` pour toutes les commandes créées ce lot ; le champ existe dans le modèle pour une compatibilité future sans être exploité.

## 7. Administration

`ShippingMethodsController`/`TaxRatesController` (`[Authorize(Roles = "Admin")]`) — liste/création/édition, jamais de suppression physique, suivant exactement le patron établi par `BrandsController` (COSMECHIC-CATALOG-001) : désactivation (`IsActive = false`) au lieu de `DELETE`, cohérent avec `FK_OrderHeaders_ShippingMethods` en `Restrict`.

## 8. Amorçage des valeurs par défaut

`CommerceSeedService` (même patron que `CatalogBackfillService`, idempotent, non bloquant au démarrage) crée, si aucune ligne n'existe :
- Une méthode « Livraison standard » à 15,00 $ (valeur pré-existante migrée depuis la vue, §1).
- Deux `TaxRate` : TPS fédérale 5 % (`RegionCode = NULL`), TVQ Québec 9,975 % (`RegionCode = "QC"`) — valeurs pré-existantes migrées depuis la vue, §1.

Aucune autre juridiction, seuil de livraison gratuite ou méthode supplémentaire n'est inventée.

## 9. Impact sur les emails de confirmation

Aucun changement effectué ce lot (hors scope COMMUNICATIONS-001). Vérification : `SmtpEmailSender`/les flux d'email existants (COSMECHIC-IDENTITY-COMMS-001) ne construisent aucun contenu à partir de `OrderHeader.OrderTotal` ou d'un récapitulatif financier — aucun email de confirmation de commande avec ventilation de prix n'existe actuellement dans le code. Rien à corriger ; à réévaluer si un tel email est introduit dans un lot futur.

## 10. Tests

| Fichier | Portée |
|---|---|
| `CheckoutServiceTests.cs` (mis à jour + étendu) | Calcul serveur Subtotal/Shipping/Tax/Discount/Total, snapshot méthode, méthode invalide/désactivée, seuil de livraison gratuite (au/sous le seuil), aucune juridiction configurée, taux désactivé, arrondi limite (`AwayFromZero` vs bancaire), somme des lignes Stripe = OrderTotal en cents. |
| `ShippingCalculatorTests.cs` (nouveau) | Matrice directe : méthode inexistante, désactivée, sans seuil, sous/à/au-dessus du seuil de livraison gratuite. |
| `TaxCalculatorTests.cs` (nouveau) | Matrice directe : aucun taux, taux régional seul, taux national (RegionCode NULL) sur toute région, Québec (2 lignes sommées), taux désactivé, taux pas encore effectif, taux expiré, pays différent, arrondi `AwayFromZero`. |
| `RegionCodeResolverTests.cs` (nouveau) | Variantes Québec (accents/casse/espaces), null/vide, code à 2 lettres, texte libre non reconnu. |
| `CheckoutTotalsHttpTests.cs` (nouveau) | Bout en bout HTTP réel (`CustomWebApplicationFactory`) : checkout valide, **tampering** (champs financiers/état supplémentaires postés → aucun effet, total reste celui du serveur), méthode invalide/désactivée rejetée sans commande ni appel Stripe, utilisateur anonyme rejeté. |
| `SqlServerConstraintTests.cs` (étendu) | `CK_OrderHeaders_Total_Equals_Components` rejette une ligne incohérente et accepte une ligne cohérente contre un vrai SQL Server ; une commande référençant une `ShippingMethod` ensuite désactivée reste consultable (FK Restrict, pas Cascade). |
| `SqlServerFulfillmentConcurrencyTests.cs`, `StripeWebhookControllerTests.cs`, `StripeFulfillmentServiceTests.cs`, `Infrastructure/TestDataSeeder.cs` | Adaptés (`Subtotal` seedé en cohérence avec `OrderTotal`) — aucune régression, comportement de fulfillment/webhook inchangé. |

```
TESTS=177/177 PASS (dont 26 SQL Server jetable réel)
NEW_TESTS=41
BASELINE_TESTS=136/136 (CATALOG-001, toujours PASS)
```

## 11. Gates finaux

```
BUILD=PASS (0 erreur)
WARNING_BASELINE (1e2f460, --no-incremental)=114 (CS8618:50, CS8602:24, CS8600:12, CS8601:10, CS8604:6, CS8625:4, ASP0019:4, CS8619:2, CS1998:2)
WARNING_CURRENT (--no-incremental)=100 (CS8618:50, CS8602:18, CS8600:10, CS8604:6, CS8625:4, CS8601:4, ASP0019:4, CS8619:2, CS1998:2)
WARNING_DELTA=-14 (aucun nouveau code d'avertissement introduit ; diminution nette, probablement liée à la réécriture de Cart/Summary.cshtml et CartController)
VULNERABLE_PACKAGES=0 (dotnet list package --vulnerable --include-transitive)
MIGRATION_MODEL_DRIFT=NONE (ApplicationDbContext et CosmechicsContext)
SQL_SERVER_VALIDATION=PASS (migration appliquée sur base historique simulée, invariant CHECK vérifié réel)
```
