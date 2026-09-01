# COSMECHIC-CATALOG-001 — Fondation catalogue produit : recherche, filtres, données cosmétiques, images multiples

- **Lot** : COSMECHIC-CATALOG-001
- **Base de départ** : `12bae6a` (COSMECHIC-SECURITY-002, PASS)
- **Portée** : SEARCH-001, recherche/filtres/tri/pagination réels, SKU, slugs produit/catégorie, marque, enrichissement cosmétique (INCI/mode d'emploi/précautions/quantité nette), images multiples sécurisées, administration catalogue correspondante, suppression sûre, tests.
- **Hors scope, volontairement non touché** : refonte générale du site, shipping/taxes/retours/wishlist/newsletter/blog/SEO complet (JSON-LD, sitemap)/pages légales/DevOps/analytics/recommandation.

## 0. Recertification technique

```
LOCAL_HEAD=12bae6a79a9b8244843f391b5e68cd10aacdb985
WORKTREE=CLEAN
RESTORE=PASS, BUILD=PASS (0 erreur), TESTS=116/116 PASS
```

## 1. Recertification du catalogue existant

| Capability | État avant | Contrôleur/Vue | Problème |
|---|---|---|---|
| Recherche | Cassée | `ProduitsController.Rechercher` → `View("ResultatsRecherche", ...)` | Vue absente : crash systématique hors correspondance exacte unique. Aucune UI ne pointait vers cette action (inatteignable). |
| Pagination | Partielle | `ViewModels/PaginatedList.cs` | Fonctionne (Skip/Take côté SQL) mais non bornée (page/pageSize arbitraires acceptés) ; jamais utilisée par la recherche. |
| Filtres/tri | Absents | — | Aucun filtre catégorie/prix/disponibilité/marque ; aucun tri. |
| Slugs produit/catégorie | Absents | — | Routes uniquement par ID. |
| SKU | Absent | — | Aucun identifiant produit stable indépendant du nom. |
| Marque | Absente | — | Aucune structure ; cosmétiques fortement dépendants de la marque. |
| Images multiples | Absentes | `Produit.Image` (string unique) | Un seul visuel par produit ; `Models/Picture.cs` existe mais n'est *pas* enregistré comme DbSet — code mort, non réutilisé (ambiguïté Catégorie/Produit dans son propre modèle). |
| Suppression produit | Dangereuse | `ProduitsController.DeleteConfirmed` | Suppression physique inconditionnelle → `DbUpdateException` non intercepté si le produit a un historique (`FK_OrderDetails_Produits`/`FK_Avis_Produits`, `ClientSetNull` sur colonne non-nullable = `NO ACTION` réel côté SQL Server). |
| Suppression catégorie | Dangereuse | `CategoriesController.DeleteConfirmed` | Même défaut (`FK_Produits_Categories`). |
| Champs cosmétiques | Absents | — | Aucun (INCI, mode d'emploi, précautions, quantité nette). |
| Création produit (admin) | **Cassée** (découverte ce lot) | `ProduitsController.Create` POST | `Produit.Categorie` (navigation) et `Produit.RowVersion` sont des types référence non-nullables jamais liés depuis le formulaire → validation implicite ASP.NET Core rejetait **toute** création de produit via le formulaire existant, avant même ce lot. Jamais détecté faute de test d'intégration HTTP réel sur cette action. |

## 2. SEARCH-001

```
SEARCH_001_REPRODUCED=YES
ROOT_CAUSE=Cosmechic/Views/Produits/ResultatsRecherche.cshtml n'existait pas ; View("ResultatsRecherche", ...) levait une InvalidOperationException à l'exécution dès que la recherche ne retournait pas exactement 1 correspondance exacte sur Nom.
```

Corrigé par une réécriture complète de `ProduitsController.Rechercher` (voir §3) et création de la vue manquante avec gestion explicite des 3 états (0/1/N résultats), plus une entrée UI réelle (barre de recherche dans `_Layout.cshtml`, absente avant ce lot — l'action n'était atteignable par aucun lien).

## 3. Recherche, filtres, tri, pagination

`CatalogSearchViewModel` (Query/Filters/Sort/Page/TotalPages/TotalResults/Products/AvailableCategories/AvailableBrands) — état entièrement dérivable de la querystring (GET uniquement, "bookmarkable").

**Pipeline SQL** (`ProduitsController.Rechercher`) : `IQueryable` → critères de recherche → filtres → tri → `CountAsync()` → `Skip/Take` → matérialisation. Aucun `ToList()` prématuré.

**Recherche insensible à la casse et aux accents** : `EF.Functions.Collate(p.Nom, "Latin1_General_CI_AI").Contains(term)`. Vérifié empiriquement contre SQL Server réel (Docker) — **avant** de choisir cette approche : le cast `COLLATE` seul (sans `EF.Functions.Collate` dans une clause `LIKE`) ne translittère PAS la chaîne (`Café` reste `Café`), seule la comparaison via `COLLATE ... LIKE` fonctionne réellement. Porte sur Nom, Description, Categorie.Nom, Brand.Nom, Sku.

**Filtres** : catégorie, marque, prix min/max (validés `>= 0`, `min <= max`, `decimal`), disponibilité (`Disponible && Stock > 0`).

**Tri** : `relevance` (défaut — tri stable par nom, assumé honnêtement comme *non* un vrai scoring de pertinence, aucun moteur de ce type n'existant), `price_asc`, `price_desc`, `name_asc`, `newest` (basé sur `Produit.DateCreation`, nouveau champ — jamais simulé via l'ID).

**Pagination** : bornée (`PageSize` 1–60, défaut 20), `Page`/`PageSize` invalides normalisés plutôt que rejetés (jamais d'exception), filtres conservés à chaque changement de page (`asp-all-route-data`).

**État "aucun résultat"** : page dédiée avec effacement de la recherche / retour au catalogue — jamais d'exception ni de page blanche.

Testé contre SQL Server réel (`CatalogSearchSqlServerTests.cs`, 9 tests) : nom exact/partiel avec accents, aucun résultat, filtre catégorie, filtre prix, disponibilité, tri asc/desc, nouveautés, pagination, normalisation page/pageSize invalides.

## 4. Modèle produit — audit et décisions

```
CURRENT_PRODUCT_FIELDS=ProduitId, Nom, CategorieId, Description, Prix, Stock, Disponible, Image, NombreVentes, RowVersion
```

| Champ | Classification | Décision |
|---|---|---|
| Sku | REQUIRED_NOW | Ajouté (§5) |
| Slug | REQUIRED_NOW | Ajouté (§5) |
| DateCreation | REQUIRED_NOW | Ajouté — nécessaire pour un tri "nouveautés" réel |
| BrandId | REQUIRED_NOW | Ajouté (§6) |
| IngredientsInci, UsageInstructions, Warnings, NetQuantity | REQUIRED_NOW | Ajoutés — valeur métier/réglementaire explicitement confirmée par le PM |
| SeoTitle, SeoDescription | USEFUL_NOW | Ajoutés — nullable, optionnels, coût nul, prépare une future SEO-001 |
| SkinType, HairType, Concerns | **DEFER** | Aucun vocabulaire métier validé — coder une taxonomie arbitraire aurait été le contre-exemple explicite du PM. Fondation non posée (ni enum, ni table) : rien à défaire plus tard, décision neutre. |
| CountryOfOrigin, Benefits | **DEFER** | Non explicitement demandés ; valeur incrémentale faible sans les champs structurés (SkinType/etc.) qu'ils accompagnent habituellement. Ajout futur sans risque de migration (champ nullable simple). |
| RegularPrice/SalePrice | **DEFER** | `Promotion` (déjà en base) est un système de bannière marketing générique, sans FK vers `Produit` — pas un moteur de prix concurrent. Introduire un second modèle de prix maintenant dupliquerait la logique sans qu'un système de promotion par produit soit validé ; `Prix` reste l'unique source de vérité (préserve intégralement ECOM-CORE-001). |
| LowStockThreshold | DEFER | Non nécessaire au storefront immédiat (mandat) |

## 5. SKU et Slug

**SKU** : `string?` (nullable en base, filtré unique), requis par validation applicative pour tout produit créé après ce lot (`ValidateCatalogFieldsAsync`), jamais généré arbitrairement — l'admin saisit sa propre référence commerciale.

**Slug** : `string?`, généré automatiquement depuis le nom si laissé vide (`SlugGenerator.Slugify` — normalisation Unicode NFD + suppression des marques diacritiques, pas de bricolage regex fragile), unique, jamais régénéré lors d'un renommage ultérieur (liens publiés jamais cassés).

**Rétro-remplissage** (`CatalogBackfillService`, appelé une fois au démarrage, idempotent, tolérant à une base injoignable) :
- SKU historique : `COS-{ProduitId:D5}` — interne, temporaire, explicitement identifiable comme généré (jamais un SKU métier).
- Slug historique : calculé en C# (fiable, testé) plutôt qu'en SQL — un test empirique contre SQL Server réel a montré que `CAST(... COLLATE Latin1_General_CI_AI AS VARCHAR)` ne translittère PAS les accents (contrairement à une idée reçue courante), confirmant que la logique complexe ne doit pas vivre dans la migration SQL elle-même.
- Collisions : suffixe `-2`, `-3`, ... déterministe.

Vérifié contre SQL Server réel : "Beurre de Karité Pur 250g" → `beurre-de-karite-pur-250g`, doublon → `-2`, "Crème Éclat Été à l'Açaí" → `creme-eclat-ete-a-lacai`. Idempotence confirmée (deuxième exécution : aucun changement).

## 6. Marque

Entité réelle `Brand` (BrandId, Nom, Slug, Disponible) — justifiée par le mandat : filtrage marque, administration, futur SEO par marque, plusieurs produits par marque. `Produit.BrandId` nullable, `OnDelete(Restrict)` : une marque référencée ne peut jamais être supprimée physiquement — `BrandsController` n'expose que la désactivation, jamais de suppression.

## 7. Route slug

`/produits/{slug}` et `/categories/{slug}` (redirige vers la vitrine produit de la catégorie), routes ID historiques conservées (`ProduitsController.Details(int)`, `CategoriesController.Details(int)`) — aucun lien existant cassé.

**Désambiguïsation** : la contrainte regex intégrée d'ASP.NET Core (`{param:regex(...)}`) est **insensible à la casse par défaut** (`RegexOptions.IgnoreCase`) — une première tentative avec une contrainte "minuscules uniquement" échouait silencieusement (`/produits/Index` routait vers `DetailsBySlug` au lieu de `Index`, `[a-z0-9]` matchant aussi les majuscules sous IgnoreCase). Corrigé par exclusion explicite (lookahead négatif) des noms d'action réels de chaque contrôleur. Régression vérifiée par `AdminSurfaceAuthorizationTests` (SECURITY-002, toujours PASS).

## 8. Images multiples

`ProduitImage` (ProduitImageId, ProduitId, FileName, AltText, SortOrder, IsPrimary), distincte de `Produit.Image` (conservé pour compatibilité ascendante — panier, historique, vues existantes). Réutilise intégralement `IProductImageUploadService` (COSMECHIC-SECURITY-002) : aucun nouveau chemin d'upload non sécurisé, mêmes garanties (nom généré serveur, allowlist extension+MIME+signature, taille bornée).

Invariant 0-ou-1 image primaire appliqué explicitement (`SetPrimaryImage` désactive toutes les autres dans la même opération). Suppression (`DeleteImage`) : uniquement si l'enregistrement appartient au produit de la route (ownership) et via le nom de fichier stocké en base (toujours un GUID serveur, jamais une entrée client) — aucun chemin ne peut sortir du répertoire géré. Promotion automatique de l'image suivante en primaire si l'image supprimée l'était.

Administration : `Produits/ManageImages/{id}` (ajout/suppression/définition primaire), lien depuis `Produits/Edit`.

## 9. Suppression sûre

**Produit** : un produit référencé par `OrderDetails` ou `Avis` est désormais **désactivé** (`Disponible = false`) plutôt que supprimé physiquement — avant ce lot, cette situation provoquait un `DbUpdateException` non intercepté (500). Un produit sans historique reste supprimable physiquement (avec nettoyage de ses images sur disque, `ProduitImages` étant en Cascade).

**Catégorie** : une catégorie contenant des produits est désormais bloquée avec un message clair, plutôt que de laisser remonter la même exception SQL brute.

## 10. Disponibilité

`Disponible` (existant, publié/vendable) et `Stock` (existant, quantité) restent les deux champs ; aucun `IsActive` redondant introduit. Disponibilité client dérivée de façon cohérente partout : `Disponible && Stock > 0` (recherche, fiche produit, page d'ajout au panier). Validation serveur déjà existante (ECOM-CORE-001) inchangée ; UI mise à jour pour la refléter sans jamais la remplacer (badge "Rupture de stock", bouton désactivé, `max` HTML sur la quantité).

## 11. Bug découvert et corrigé : création produit cassée

En écrivant les tests d'intégration HTTP pour la création de produit (jamais testée à ce niveau avant ce lot), découverte que `Produit.Categorie` (navigation) et `Produit.RowVersion` (jeton de concurrence), tous deux des types référence non-nullables jamais liés par le formulaire, déclenchaient la validation implicite d'ASP.NET Core sur les types référence non-nullables — bloquant **toute** création de produit via le formulaire admin existant, indépendamment de ce lot. Corrigé par `[ValidateNever]` sur les deux propriétés (jamais destinées à être saisies par un formulaire) ; `RowVersion` reçoit également un placeholder inoffensif avant insertion (ignoré par SQL Server pour une colonne `rowversion`, nécessaire uniquement pour la robustesse multi-fournisseur).

## 12. Migration

`Cosmechic/Migrations/20260901030805_EnhanceProductCatalog.cs` (`CosmechicsContext`) : purement additive — colonnes nullables + index uniques filtrés (`WHERE ... IS NOT NULL`), tables `Brands`/`ProduitImages`, FK `Produits.BrandId → Brands` (Restrict), `ProduitImages.ProduitId → Produits` (Cascade). Aucun `DROP`, aucune recréation, aucune donnée existante affectée.

Validé sur SQL Server 2022 jetable (Docker) : reconstruction complète depuis une base vide (migrations Identity puis Business), et application incrémentale sur une base déjà peuplée (rétro-remplissage vérifié, voir §5).

```
DATABASE_RECONSTRUCTIBLE=YES
MODEL_MIGRATION_DRIFT=NONE (les deux contextes)
```

## 13. Tests ajoutés

| Fichier | Tests | Couverture |
|---|---|---|
| `CatalogSearchSqlServerTests.cs` | 9 | Recherche accent-insensible, aucun résultat, filtre catégorie/prix/disponibilité, tri prix asc/desc/nouveautés, pagination, normalisation page/pageSize (SQL Server réel — `EF.Functions.Collate` non traduisible par InMemory) |
| `CatalogAdminTests.cs` | 11 | SKU requis/unique, slug auto-généré, Edit ne réinitialise pas DateCreation, suppression produit avec/sans historique, suppression catégorie bloquée, autorisation Brands (admin uniquement), création marque, image ajoutée devient primaire automatiquement, invariant 0-ou-1 primaire |

**Total nouveaux tests : 20.** Régression complète (SECURITY-001 → SECURITY-002 + ce lot) : **136/136 PASS**, dont 23 tests SQL-Server-réels au total (constraint/concurrency hérités + recherche catalogue).

## 14. Portes de qualité

```
BASELINE_SHA=12bae6a79a9b8244843f391b5e68cd10aacdb985
RESTORE=PASS
BUILD=PASS (0 erreur)
TESTS_BEFORE=116
TESTS_AFTER=136
TESTS_FAIL=0
NUGET_CRITICAL=0, NUGET_HIGH=0, NUGET_MODERATE=0, NUGET_LOW=0 (aucune dépendance ajoutée)
WARNING_DELTA=0 nouvelle occurrence nette (114 occurrences avant/après, comparaison worktree git normalisée par fichier+code)
DATABASE_RECONSTRUCTIBLE=YES
MODEL_MIGRATION_DRIFT=NONE
SECURITY_REGRESSION=NONE (upload SECURITY-002 réutilisé tel quel, aucun nouveau chemin média)
ECOM_CORE_REGRESSION=NONE (Prix reste l'unique champ prix, aucune modification de StripeFulfillmentService/OrderCheckoutService)
IDENTITY_REGRESSION=NONE (aucune modification ApplicationDbContext/Identity)
PRODUCTION_TOUCHED=NO
PUSHED=NO
```

## 15. Écarts restants (documentés, non bloquants)

- **Taxonomie cosmétique** (SkinType/HairType/Concerns) : fondation volontairement non posée, en attente d'un vocabulaire métier validé.
- **Modèle de prix promotionnel** (RegularPrice/SalePrice) : reporté à un futur lot dédié, pour ne jamais dupliquer la logique de prix face à ECOM-CORE-001.
- **SEO complet** (JSON-LD, sitemap) : hors périmètre explicite de ce lot ; seuls SeoTitle/SeoDescription (nullable) posés en fondation.
- **`Models/Picture.cs`** : reste un artefact de scaffold mort (aucun DbSet), non nettoyé — hors périmètre, sans impact (jamais utilisé).
