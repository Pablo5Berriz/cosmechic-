# COSMECHIC-DATA-001 — Reconstruction du schéma de base de données, baseline de migration et intégrité monétaire

- **Lot** : COSMECHIC-DATA-001
- **Base de départ** : `a337d0917f2c9c7bb6b11dae4e4c74bcec588293` (COSMECHIC-BASELINE-2026)
- **Portée** : schéma, migrations, précision monétaire, préparation concurrence/idempotence. Aucune logique métier (panier, paiement, webhook, email, recherche) modifiée.
- **Hors scope, volontairement non touché** : EMAIL-001 (IDENTITY-COMMS-001), bug de recherche produit (CATALOG-001), duplication `StripeSettings`/ARCH-001, logique de traitement des webhooks Stripe, décrément de stock.

## 1. Architecture avant / après

### Avant
Deux `DbContext` coexistaient sans division formelle des responsabilités de migration :

| DbContext | Type | Tables couvertes | Migrations |
|---|---|---|---|
| `ApplicationDbContext` (`Cosmechic.Data`) | `IdentityDbContext<IdentityUser>` | 7 tables Identity (`AspNetUsers`, `AspNetRoles`, ...) | `Cosmechic.Data.Migrations`, 2 migrations déjà appliquées historiquement |
| `CosmechicsContext` (`Cosmechic.Models`) | `DbContext` scaffoldé database-first | Les 7 mêmes tables Identity (POCOs dupliqués, ARCH-002) **+** 10 tables métier (`Produits`, `OrderHeaders`, `OrderDetails`, `ShoppingCarts`, `Avis`, `Categories`, `Promotions`, `BlogPosts`, `TemoignagesClients`) | **Aucune** — 0 migration pour ce contexte avant ce lot |

Conséquence : le schéma métier n'était reconstructible qu'en pointant l'application sur une base existante déjà peuplée à la main ; il n'existait aucun moyen reproductible de recréer la base à partir du dépôt seul (`DATABASE_RECONSTRUCTIBLE` = NON avant ce lot).

### Après — stratégie retenue
Aucun des deux `DbContext` n'a été fusionné, renommé ni refactoré. La division des responsabilités de migration a été **formalisée** avec l'API EF Core `ExcludeFromMigrations()` :

- `ApplicationDbContext` reste seul propriétaire des migrations des 7 tables Identity (inchangé, migrations historiques déjà appliquées, jamais retouchées).
- `CosmechicsContext` continue de lire/écrire les mêmes 7 tables Identity au runtime (aucune requête existante affectée — `ExcludeFromMigrations()` n'agit que sur la génération de migrations, jamais sur les requêtes), mais ne génère plus jamais de `CREATE`/`ALTER`/`DROP` pour elles.
- `CosmechicsContext` devient seul propriétaire d'une toute nouvelle baseline de migration (`InitialBusinessSchema`) pour les 10 tables métier, avec zéro risque puisqu'aucune migration n'existait avant pour ce contexte.

C'est l'option la plus proche d'une "Option A" (statu quo architectural, division de responsabilité seulement) : elle atteint l'objectif du mandat (base reconstructible depuis zéro) sans toucher au code applicatif, sans migration de données, et sans risque de perte.

**Gap connu, documenté et non résolu dans ce lot** : le schéma Identity de base créé par la migration historique de `ApplicationDbContext` ne contient pas les colonnes `StreetAddress`/`City`/`State`/`PostalCode` que le code runtime de `CosmechicsContext` (notamment `CartController.SummaryPOST`) lit/écrit sur `AspNetUser`. Une reconstruction stricte depuis zéro produit donc un schéma fonctionnellement incomplet pour cette table précise. Ce gap est antérieur à ce lot, n'a pas été introduit ni aggravé par lui, et est explicitement laissé ouvert pour une décision future gérée séparément (probablement au sein d'ECOM-CORE-001 ou d'un lot dédié), plutôt que comblé par une migration basée sur des hypothèses.

## 2. Migrations créées

| Fichier | Contexte | Contenu |
|---|---|---|
| `Cosmechic/Migrations/20260831232040_InitialBusinessSchema.cs` (+ `.Designer.cs`, `CosmechicsContextModelSnapshot.cs`) | `CosmechicsContext` | Crée les 10 tables métier, tous les `CHECK` constraints, les colonnes `money`, la colonne `rowversion`, l'index unique `StripeEventId`. `Down()` ne fait que supprimer ces mêmes 10 tables — aucune opération sur une table Identity. |

Namespace `Cosmechic.Migrations`, distinct de `Cosmechic.Data.Migrations` (celui d'`ApplicationDbContext`), conformément à la recommandation EF Core d'isoler les migrations par contexte.

## 3. Convention monétaire

Toutes les colonnes monétaires sont désormais explicitement typées `money` (cohérent avec `Produit.Prix`, déjà ainsi typé avant ce lot) :

| Entité.Propriété | Type SQL |
|---|---|
| `Produit.Prix` | `money` (déjà présent) |
| `OrderDetail.Price` | `money` (ajouté) |
| `OrderHeader.OrderTotal` | `money` (ajouté) |

Aucun `float`/`double` n'est utilisé pour une valeur monétaire. Vérifié en base réelle (voir section 5) : une valeur `12345.6789m` fait un aller-retour exact à travers la colonne `money`.

## 4. Contraintes, index, comportements de suppression

### CHECK constraints ajoutées

| Contrainte | Table | Condition |
|---|---|---|
| `CK_Produits_Stock_NonNegative` | `Produits` | `[Stock] >= 0` |
| `CK_Produits_Prix_NonNegative` | `Produits` | `[Prix] >= 0` |
| `CK_OrderDetails_Count_Positive` | `OrderDetails` | `[Count] > 0` |
| `CK_OrderDetails_Price_NonNegative` | `OrderDetails` | `[Price] >= 0` |
| `CK_OrderHeaders_OrderTotal_NonNegative` | `OrderHeaders` | `[OrderTotal] >= 0` |
| `CK_ShoppingCarts_Count_Positive` | `ShoppingCarts` | `[Count] > 0` |

### Index ajouté
`IX_ShoppingCarts_ApplicationUserId` sur `ShoppingCarts.ApplicationUserId` — justifié par l'usage réel : `CartController` filtre sur cette colonne dans quasiment chaque action (Index/Summary/Plus/Minus/Remove), et aucun index n'existait avant.

### Delete-behavior clarifié (sans changement de comportement)
`TemoignagesClient -> Produit` n'avait aucune FK explicite (découverte par convention EF, `Cascade` implicite car FK requise). Rendue explicite avec le même comportement (`Cascade`) pour cohérence de lecture — comportement identique, juste documenté au lieu d'implicite.

### Intégrité historique des commandes (préparation, non branchée)
`OrderDetail.ProduitNom` (`nvarchar(450)`, nullable) : capture prévue du nom du produit au moment de l'achat, indépendamment d'un renommage ultérieur. Colonne ajoutée au schéma uniquement — **aucun code applicatif ne la renseigne encore** (`CartController.SummaryPOST` inchangé dans ce lot). À raccorder explicitement dans COSMECHIC-ECOM-CORE-001.

## 5. Préparation concurrence et idempotence (schéma seul, zéro logique)

- **`Produit.RowVersion`** (`rowversion`, généré par SQL Server) : jeton de concurrence optimiste, nécessaire pour empêcher deux commandes concurrentes de survendre le dernier exemplaire d'un produit. Aucune logique de réservation/décrément de stock ajoutée dans ce lot — préparation explicite pour ECOM-CORE-001. Vérifié en base réelle : la valeur change automatiquement à chaque `UPDATE`, sans intervention applicative.
- **`ProcessedStripeEvent`** (nouvelle table) : modèle minimal d'idempotence webhook (`StripeEventId` unique, `EventType`, `ProcessingStatus`, référence `OrderHeader` optionnelle). Aucune donnée de carte bancaire. Aucune logique de traitement de webhook développée. Vérifié en base réelle : un second `INSERT` avec le même `StripeEventId` est rejeté par l'index unique.

## 6. Validation contre un SQL Server réel (pas seulement InMemory)

Le mandat exigeait explicitement une validation contre un SQL Server réel, InMemory étant insuffisant pour vérifier les `CHECK` constraints, les index uniques et le comportement `rowversion`.

**Procédure exécutée** (conteneur Docker jetable, `mcr.microsoft.com/mssql/server:2022-latest`, jamais de base existante/production) :
1. Démarrage d'un conteneur SQL Server 2022 jetable, port local dédié, mot de passe temporaire non versionné.
2. `dotnet ef database update --context ApplicationDbContext` sur base vide → succès, création des 7 tables Identity.
3. `dotnet ef database update --context CosmechicsContext` sur la même base → succès, création des 10 tables métier avec toutes les contraintes attendues, sans dupliquer ni toucher aux tables Identity.
4. Vérification directe en SQL (`INFORMATION_SCHEMA.TABLES`, `sys.check_constraints`, `sys.indexes`) : 18 tables exactement (7 Identity + 10 métier + `__EFMigrationsHistory`), 6 `CHECK` constraints présentes avec leur définition exacte, index unique `IX_ProcessedStripeEvents_StripeEventId` confirmé unique.
5. **Reconstruction depuis zéro** : base entièrement supprimée (`DROP DATABASE`), puis les deux jeux de migrations réappliqués dans le même ordre sur une base vide — succès identique, 18 tables retrouvées à l'identique.
6. **Vérification de dérive modèle/migration** : `dotnet ef migrations has-pending-model-changes` pour les deux contextes → `No changes have been made to the model since the last migration.` dans les deux cas.
7. Conteneur et identifiants temporaires détruits en fin de validation — aucune trace laissée.

`DATABASE_RECONSTRUCTIBLE=OUI`, `MODEL_MIGRATION_DRIFT=NONE`.

## 7. Tests

### Régression (COSMECHIC-SECURITY-001)
Les 35 tests existants ont d'abord échoué après l'ajout de `Produit.RowVersion` : le fournisseur InMemory (utilisé par les tests) ne génère pas automatiquement les colonnes `rowversion` comme le fait SQL Server, et exigeait donc une valeur explicite au moment du seed. Corrigé en fournissant une valeur factice dans `TestDataSeeder` (`Cosmechic.Tests/Infrastructure/TestDataSeeder.cs`), strictement scoping aux données de test — aucun changement de modèle ni de comportement applicatif. Les 35 tests passent à nouveau intégralement après correction.

### Nouveaux tests ajoutés (15)

**`Cosmechic.Tests/DataModelMetadataTests.cs`** (9 tests, métadonnées du modèle EF Core, pas de connexion réelle) :
- Construction du modèle `CosmechicsContext` sans erreur.
- `Produit.Prix`, `OrderDetail.Price`, `OrderHeader.OrderTotal` configurés en type colonne `money`.
- `Produit.RowVersion` configuré comme jeton de concurrence à génération automatique.
- Relations `OrderDetail -> OrderHeader` et `OrderDetail -> Produit` requises.
- Index unique sur `ProcessedStripeEvent.StripeEventId`.
- FK `ProcessedStripeEvent -> OrderHeader` optionnelle.
- Les 6 tables Identity sont bien marquées exclues des migrations pour `CosmechicsContext`.

**`Cosmechic.Tests/SqlServerConstraintTests.cs`** (6 tests, contre un vrai SQL Server 2022 jetable, démarré et détruit automatiquement par la fixture) :
- Rejet d'un `Produit.Stock` négatif par le `CHECK`.
- Rejet d'un `Produit.Prix` négatif par le `CHECK`.
- Rejet d'un `ShoppingCart.Count` ≤ 0 par le `CHECK`.
- Rejet d'un `StripeEventId` dupliqué par l'index unique.
- La colonne `RowVersion` change réellement de valeur à chaque `UPDATE`, générée par le moteur.
- Une valeur `money` à 4 décimales fait un aller-retour exact.

**`Cosmechic.Tests/Infrastructure/SqlServerFixture.cs`** : fixture xUnit (`IAsyncLifetime` + `ICollectionFixture`) qui démarre/détruit le conteneur Docker jetable et applique les deux jeux de migrations. Si Docker est indisponible dans l'environnement d'exécution, les tests dépendants se désactivent proprement (message explicite, aucune assertion exécutée) plutôt que de faire échouer `dotnet test` pour tout contributeur sans Docker.

**Total** : 50/50 tests passent (35 SECURITY-001 + 9 métadonnées + 6 intégration SQL Server réelle).

## 8. Correction annexe nécessaire : garde `OnConfiguring` manquante

`CosmechicsContext.OnConfiguring` appelait `UseSqlServer(...)` sans la garde standard `if (!optionsBuilder.IsConfigured)` généralement présente dans le code scaffoldé par `dotnet ef dbcontext scaffold`. Sans effet visible dans l'hôte ASP.NET Core réel (Program.cs résout la même valeur de connexion via la configuration), mais bloquait toute construction autonome du contexte hors du pipeline applicatif — empêchant précisément les tests d'intégration SQL Server réels exigés par ce lot. Correction d'une ligne, comportement de production strictement inchangé, nécessaire pour livrer la validation SQL Server mandatée.

## 9. Warnings

- Avant (baseline établie, rebuild propre `rm -rf bin obj`) : **76** avertissements (`WARNINGS_BEFORE`).
- Après (même méthode) : **78** au total selon le résumé MSBuild, mais l'écart provient entièrement de la duplication, au niveau du log de restauration NuGet, des deux avertissements préexistants `NU1902` (vulnérabilités connues de `MailKit`/`MimeKit`, non liées à ce lot, aucun package touché) — un artefact de restauration à froid (`rm -rf bin obj`), pas une régression de code. Comparaison ligne à ligne des avertissements `CS*`/`ASP*` (diagnostics réellement liés au code) entre avant et après : **strictement identiques, 0 différence**.
- `NEW_WARNINGS` (code) = **0**.

## 10. Risques restants / suites à donner

- Gap Identity StreetAddress/City/State/PostalCode (section 1) — décision à prendre séparément, hors DATA-001.
- `OrderDetail.ProduitNom` préparé mais non alimenté — à raccorder dans ECOM-CORE-001.
- `Produit.RowVersion` préparé mais aucune logique de réservation/décrément de stock ne l'utilise encore — à implémenter dans ECOM-CORE-001.
- `ProcessedStripeEvent` préparé mais aucun traitement de webhook ne l'utilise encore — à implémenter dans ECOM-CORE-001.
- EMAIL-001, bug de recherche produit, doublon `StripeSettings` (ARCH-001) : ouverts, non touchés, confirmés hors périmètre de ce lot.
