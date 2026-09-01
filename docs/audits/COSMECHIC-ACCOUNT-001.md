# COSMECHIC-ACCOUNT-001 — Espace client, profil, adresses, self-service commandes

Baseline : `0d68dfb` (COSMECHIC-COMMERCE-OPERATIONS-001B-CLOSURE-1).

## 1. Recertification de la baseline

```
RESTORE=PASS
BUILD=PASS (0 erreur, Release)
TESTS=240/240 PASS
VULNERABLE_PACKAGES=0
```
Aucune divergence constatée avant implémentation.

## 2. Audit fonctionnel de l'espace compte existant (avant ce lot)

| Route | Auth | Objectif | État | Problème |
|---|---|---|---|---|
| `/Identity/Account/Login`, `Register`, `Logout`, `ForgotPassword`, `ResetPassword`, `ConfirmEmail` | Anonyme/Authentifié | Flux Identity standard | VALIDÉ | Aucun (déjà audités IDENTITY-COMMS-001) |
| `/Identity/Account/Manage/Index` | Authentifié | Nom d'utilisateur + téléphone (via `UserManager.SetPhoneNumberAsync`) | VALIDÉ | Aucun — pattern sûr réutilisé tel quel |
| `/Identity/Account/Manage/Email`, `ChangePassword`, `TwoFactorAuthentication`, `ExternalLogins`, `PersonalData` | Authentifié | Sécurité du compte | VALIDÉ | Aucun — conservés inchangés, reliés depuis `Account/Profile` |
| `/Identity/Account/Manage/DownloadPersonalData` | Authentifié | Export de données personnelles | PARTIEL | N'exporte que les champs `[PersonalData]` d'`IdentityUser` (Id/UserName/Email/PhoneNumber) — aucune commande/adresse/retour. Documenté §7, non étendu (hors périmètre explicite). |
| `/Identity/Account/Manage/DeletePersonalData` | Authentifié | Suppression de compte | CASSÉ | `_userManager.DeleteAsync(user)` sans garde : pour un client ayant déjà commandé, la contrainte FK réelle `FK_OrderHeaders_AspNetUsers` (NO ACTION, `ApplicationUserId` non-nullable) fait échouer la suppression avec une `SqlException` non gérée (erreur 500). **Corrigé dans ce lot (§7).** |
| `/AspNetUsers/Index`, `Details` | Authentifié (client sur son propre profil) | "Profil" (lien nav historique) | CASSÉ | `Details`/`Index` exposaient l'entité `AspNetUser` complète (y compris colonnes Identity) au client. |
| `/AspNetUsers/Edit` | Authentifié (client sur son propre profil) | Modifier nom/téléphone/adresse | **CASSÉ (critique)** | `[Bind("Id,UserName,Email,PhoneNumber,StreetAddress,City,State,PostalCode")]` sur l'entité `AspNetUser`, puis `_context.Update(aspNetUser)` — met à jour TOUTES les colonnes comme "Modified", écrasant silencieusement `PasswordHash`/`SecurityStamp`/`ConcurrencyStamp`/`NormalizedUserName`/`NormalizedEmail`/`EmailConfirmed`/`PhoneNumberConfirmed`/`TwoFactorEnabled`/`LockoutEnd`/`LockoutEnabled`/`AccessFailedCount` avec leur valeur CLR par défaut. Un client modifiant son propre profil se retrouvait avec un compte cassé (mot de passe nul, recherche par email/username normalisé rompue). **Corrigé dans ce lot (§7).** |
| `/OrderHeaders/Index`, `Details` | Authentifié (ownership) | Historique/détail de commande | PARTIEL | Ownership déjà correct (SECURITY-001), mais pas de pagination, tableau HTML non responsive (défilement horizontal sur mobile), mélange colonnes admin/client, aucune intégration adresses/retours/remboursements. |
| `/Cart/Summary`, `SummaryPOST` | Authentifié | Checkout | PARTIEL | Ne préremplissait que Nom/Téléphone (jamais l'adresse), aucune intégration adresse enregistrée. **Étendu dans ce lot (§6).** |
| `/Cart/Receipt` | Owner ou Admin | Reçu | VALIDÉ | Déjà correct (COMMERCE-OPERATIONS-001B) |
| `/Cart/CancelOrder` | Authentifié (ownership) | Annulation | VALIDÉ | Délègue déjà à `ICancellationService` — mais la vue dupliquait la règle d'éligibilité (`Model.OrderStatus != "Cancelled" && ...`) au lieu de l'interroger. **Corrigé §8.** |
| `/Returns/Request`, `Details` | Authentifié (ownership) | Demander/consulter un retour | VALIDÉ (individuel) | Aucune liste "mes retours" — ABSENT jusqu'à ce lot. |
| Remboursements | — | — | ABSENT | Aucune surface client — visibles uniquement via `OrderOperationsController` (Admin). |
| Adresses multiples | — | — | ABSENT | Seuls 4 champs plats sur `AspNetUsers` (profil), non utilisés par le checkout, sans notion de "par défaut" ni de plusieurs adresses. |
| Tableau de bord compte | — | — | ABSENT | Aucune page d'accueil du compte. |

## 3. Stratégie d'adresses — audit avant conception

```
CURRENT_ADDRESS_MODEL=4 champs plats (StreetAddress/City/State/PostalCode) en shadow properties sur IdentityUser (ApplicationDbContext, migration AddCustomerAddressFields d'IDENTITY-COMMS-001), surfacés en lecture/écriture via CosmechicsContext.AspNetUser
CURRENT_ADDRESS_USAGE=Profil uniquement — CartController.Summary ne préremplissait QUE Name (=UserName) et PhoneNumber ; StreetAddress/City/State/PostalCode n'étaient consultés par aucun flux de checkout avant ce lot
ORDER_ADDRESS_SNAPSHOT=OrderHeader porte déjà son propre snapshot plat (Name/PhoneNumber/StreetAddress/City/State/PostalCode), indépendant de toute FK vers le profil — immuabilité déjà garantie par construction depuis COMMERCE-OPERATIONS-001A
MULTIPLE_ADDRESSES_SUPPORTED=NON (un seul jeu de champs par utilisateur, aucune notion de "par défaut")
```

Décision : introduire `CustomerAddress` (CosmechicsContext) comme remplacement pour l'usage client réel (§4), sans toucher aux 4 colonnes historiques d'`AspNetUsers`/`ApplicationDbContext` (aucune modification Identity non nécessaire, section 40). Le CRUD client de ces 4 anciens champs (`AspNetUsersController.Edit`) est retiré du client (désormais Admin uniquement, §7) plutôt que réparé pour un usage qui n'a plus lieu d'être.

## 4. Modèle `CustomerAddress`

```csharp
CustomerAddress {
    Id, ApplicationUserId, Label, RecipientName, PhoneNumber,
    StreetAddress, City, State, PostalCode, CountryCode (défaut "CA"),
    IsDefaultShipping, CreatedAt, UpdatedAt
}
```

- FK vers `AspNetUsers` avec `DeleteBehavior.Cascade` (contrairement à `OrderHeader`/`ReturnRequest`/`Refund` qui restent `Restrict`) : une adresse enregistrée n'est **jamais** un enregistrement commercial historique (`OrderHeader` ne la référence jamais par FK, seulement par copie de valeurs) — supprimer un compte peut donc légitimement supprimer ses adresses.
- **Invariant "au plus une adresse par défaut par utilisateur" appliqué par le moteur** : index unique filtré `IX_CustomerAddresses_ApplicationUserId_DefaultShipping` (`UNIQUE (ApplicationUserId) WHERE IsDefaultShipping = 1`) — même stratégie que `IX_Refunds_StripeRefundId` (COMMERCE-OPERATIONS-001B). Un second index non filtré `IX_CustomerAddresses_ApplicationUserId` sert les requêtes de liste ordinaires. Les deux existent bien comme deux index distincts en base (vérifié dans la migration générée) — EF Core aurait sinon fusionné deux `HasIndex` successifs sur la même colonne en un seul index reconfiguré ; la surcharge `HasIndex(expr, name)` à deux arguments a été utilisée pour forcer leur distinction.
- `SUPPORTED_SHIPPING_COUNTRIES=CA` — `AddressService` rejette explicitement tout `CountryCode` autre que `RegionCodeResolver.CountryCodeCanada`, sans prétendre à un support international que le calcul de taxe/livraison ne sait pas honorer (section 12).

## 5. `IAddressService`/`AddressService`

Seule source de vérité pour le CRUD adresses — ownership systématique (`GetOwnedAsync`), jamais laissé à la charge d'un controller seul.

- `CreateAsync` : la première adresse d'un utilisateur devient automatiquement par défaut (évite l'état "adresse existante mais aucune par défaut" pour le checkout).
- `DeleteAsync` : si l'adresse supprimée était la valeur par défaut, une autre est automatiquement promue s'il en reste (jamais 0 par défaut alors qu'il en existe encore, jamais 2).
- `SetDefaultAsync`/toute promotion en "par défaut" passe par `SetAsDefaultWithRetryAsync` : transaction courte (désactive tous les défauts existants de l'utilisateur puis active la cible), avec retry (jusqu'à 3 tentatives) sur violation de l'index unique filtré — même motif "reset transactionnel + retry" que `RefundOrchestrationService`/`RestockService`. Testé sous concurrence réelle contre SQL Server (§10).
- Aucune décision de politique n'est prise côté vue/controller : `AccountController` ne fait qu'appeler le service et rediriger.

## 6. Intégration checkout

`CheckoutFormInput.SelectedAddressId` (nullable) : si renseigné, `CartController.SummaryPOST` charge la `CustomerAddress` via `IAddressService.GetOwnedAsync` (ownership vérifié — une IDOR échoue silencieusement vers "adresse introuvable", testé §10) et construit le `ShippingAddress` à partir de son contenu ; sinon, comportement inchangé (saisie ponctuelle). Dans tous les cas, `OrderCheckoutService` continue de ne recevoir qu'un `ShippingAddress` — un simple ensemble de valeurs, jamais une FK vers `CustomerAddress`. `Views/Cart/Summary.cshtml` propose une sélection d'adresse enregistrée (préremplissage par l'adresse par défaut) ou "Nouvelle adresse" ; le serveur revalide tout indépendamment de ce qui est affiché.

```
CHECKOUT_SAVED_ADDRESS_INTEGRATION=OUI (sélection ou saisie ponctuelle, ownership revérifiée serveur)
HISTORICAL_ORDER_ADDRESS_IMMUTABLE=OUI (aucune FK OrderHeader->CustomerAddress ; testé explicitement §10 — modifier l'adresse enregistrée après une commande laisse cette commande inchangée)
```

## 7. Profil et suppression de compte

- `AccountController.Profile` : DTO étroit `ProfileInput { PhoneNumber }`, persistance via `UserManager.SetPhoneNumberAsync` (même mécanisme que `Identity/Manage/Index`, aucun second système). Email/mot de passe/2FA/PersonalData restent gérés par les pages Identity existantes, reliées depuis la vue (section 8/9 : réutiliser, ne pas dupliquer).
- **`AspNetUsersController` redevient un CRUD scaffold strictement administratif** (`Index`/`Details`/`Edit` désormais `[Authorize(Roles="Admin")]`) — le client n'y accède plus, éliminant le chemin qui exposait le bug ci-dessous à un client authentifié.
- **Correctif du bug critique découvert en §2** : `Edit` POST est désormais lié à un DTO étroit (`AspNetUserEditInput { Id, PhoneNumber, StreetAddress, City, State, PostalCode }`, volontairement sans `UserName`/`Email` — les modifier correctement exige `UserManager.SetUserNameAsync`/`SetEmailAsync`, hors périmètre) plutôt qu'à l'entité `AspNetUser` ; le handler charge l'entité existante et n'y reporte que les six champs autorisés (même correctif que `OrderHeadersController.Edit`, COMMERCE-OPERATIONS-001B-CLOSURE-1) — jamais de `_context.Update()` aveugle. Recertifié même pour Admin : aucune raison légitime qu'un Admin puisse, via ce formulaire, écraser `PasswordHash`/`SecurityStamp`/etc.
- `Views/AspNetUsers/Edit.cshtml` : `UserName`/`Email` affichés en lecture seule (`<dl>`) plutôt qu'en champs de formulaire désormais ignorés par le POST — même principe de clôture que CLOSURE-1 (« aucun champ de formulaire ne doit présenter une capacité qu'il n'accepte pas réellement »).
- **Suppression de compte (`DeletePersonalData`)** : `_businessContext.OrderHeaders.AnyAsync(...)` vérifié avant tout appel à `_userManager.DeleteAsync` — si le client a un historique de commandes, la suppression est bloquée avec un message clair au lieu de l'exception SQL non gérée précédente. Politique **technique** minimale uniquement (aucune décision juridique/anonymisation inventée) :

```
ACCOUNT_DELETION_STRATEGY=Suppression autorisée uniquement si aucun historique de commande (OrderHeaders vide pour cet utilisateur) ; sinon blocage explicite avec message
LEGAL_PRIVACY_CONFIGURATION_REQUIRED=Anonymisation/rétention minimale pour un client avec historique de commande = TODO_REQUIRES_BUSINESS_CONFIGURATION (décision juridique/métier, pas technique)
PERSONAL_DATA_EXPORT_SCOPE=DownloadPersonalData n'exporte que les champs [PersonalData] d'IdentityUser — commandes/adresses/retours non inclus = TODO_REQUIRES_BUSINESS_CONFIGURATION si une conformité complète est requise
```

## 8. `ICancellationService.CanCancel` — élimination d'une duplication de règle

`Views/OrderHeaders/Details.cshtml` réimplémentait la politique d'éligibilité à l'annulation directement dans la vue (`Model.OrderStatus != "Cancelled" && Model.FulfillmentStatus is not (...) && ...`). Ajout d'une méthode pure `CanCancel(OrderHeader) : CancellationEligibility` sur `ICancellationService` (même motif que `IReturnService.CanRequestReturnAsync`, section 20/22), extraite des trois portes techniques déjà présentes dans `CancelOrderAsync`. Les deux vues (`OrderHeaders/Details.cshtml` et la nouvelle `Account/OrderDetails.cshtml`) interrogent désormais ce service au lieu de dupliquer la règle.

## 9. Espace compte — architecture et routes

Nouveau `AccountController` ([Authorize], client authentifié) :

| Route | Objectif |
|---|---|
| `GET /Account/Index` | Tableau de bord (identité, email + confirmation, 5 dernières commandes, commandes en cours, adresse par défaut) |
| `GET/POST /Account/Profile` | Coordonnées (téléphone) + liens vers Identity pour email/mot de passe/2FA |
| `GET /Account/Addresses` | Liste des adresses |
| `GET/POST /Account/CreateAddress`, `EditAddress/{id}` | CRUD adresse (vue partagée `AddressForm.cshtml`) |
| `POST /Account/DeleteAddress`, `SetDefaultAddress` | Suppression / définition par défaut |
| `GET /Account/Orders?page=` | Historique paginé (10/page) |
| `GET /Account/OrderDetails/{id}` | Détail orienté client : articles/SKU/prix/quantités, sous-total/livraison/taxes/rabais/total, adresse snapshot, statuts (commande/paiement/expédition), tracking, retours liés, remboursements liés, liens reçu/retour/annulation |
| `GET /Account/Returns` | Liste "mes retours" (nouveau — absent avant ce lot) |

Navigation : partiel `Views/Account/_AccountNav.cshtml` (Aperçu/Profil/Adresses/Commandes/Retours/Sécurité), `nav-pills` Bootstrap `flex-md-column` — ligne repliable sur mobile (jamais de défilement horizontal), colonne sur desktop, landmark `<nav aria-label>` + `aria-current="page"` sur l'entrée active. `_LoginPartial.cshtml` mis à jour : les liens "Profil"/"Commande" du client pointent désormais vers `/Account/Index`/`/Account/Orders` (au lieu de l'ancien `/AspNetUsers/Index`, désormais Admin-only).

Aucune logique de cycle de vie n'est dupliquée : annulation → `CartController.CancelOrder` existant (lien direct depuis `Account/OrderDetails`), demande de retour → `ReturnsController.Request` existant, reçu → `Cart/Receipt` existant. `AccountController` ne fait que lire/présenter et déléguer les CRUD d'adresse à `IAddressService`.

Reçu/retours/remboursements client réutilisent exactement les entités déjà en place (`ReturnRequest.Refunds`, `OrderHeader.Refunds`) — jamais `AdminComment`, `IdempotencyKey`, `StripeRefundId` ou `FailureCode` exposés côté client (uniquement montant/statut/date).

## 10. Sécurité

### 10.1 Matrice d'autorisation

| Action | Anonyme | Owner | Autre client | Admin |
|---|---|---|---|---|
| `Account/Index`, `Profile`, `Addresses`, `Orders`, `Returns` | 401 | 200 | N/A (scope implicite à l'utilisateur courant) | N/A (surface client) |
| `Account/OrderDetails/{id}` | 401 | 200 | 403 | admin passe par `OrderOperationsController` |
| `Account/EditAddress/{id}`, `DeleteAddress`, `SetDefaultAddress` | 401 | 200/succès | 404/aucun effet (IDOR) | — |
| `Cart/Receipt/{id}` | 401 | 200 | 403 | 200 (COMMERCE-OPERATIONS-001B, inchangé) |
| `Cart/CancelOrder` | 401 | selon `CanCancel` | rejeté (`CancellationRejected`, ownership) | — |
| `Returns/Request`, `Details` | 401 | 200 | 403 (inchangé, COMMERCE-OPERATIONS-001B) | — |
| `AspNetUsers/Index`, `Details`, `Edit` | 401 | **403 (nouveau)** | 403 | 200 |

### 10.2 IDOR

Testés explicitement (Customer A vs Customer B) : adresse (edit/delete/setDefault d'une adresse étrangère → 404 ou aucun effet), commande (`Account/OrderDetails` étranger → 403), checkout (`SelectedAddressId` étranger → refusé, aucune commande créée), `AspNetUsers/Edit` (désormais 403 pour tout client).

### 10.3 Overposting

`ProfileInput`, `AddressFormInput`, `AspNetUserEditInput` sont des DTOs étroits — aucun ne porte `ApplicationUserId`/`OrderTotal`/`OrderStatus`/`PaymentStatus`/`RefundStatus`/`Role`/`IsDefaultShipping` d'une ressource étrangère. Testé explicitement : POST `Account/Profile` avec `Email`/`Id`/`EmailConfirmed` supplémentaires → aucun effet ; POST `AspNetUsers/Edit` (Admin) avec `PasswordHash`/`SecurityStamp`/`EmailConfirmed`/`UserName`/`Email` supplémentaires → aucun effet, seuls les 5 champs légitimes sont appliqués.

### 10.4 CSRF / XSS

Toute mutation (`Profile`, `CreateAddress`/`EditAddress`/`DeleteAddress`/`SetDefaultAddress`) est `[HttpPost][ValidateAntiForgeryToken]`. Aucun `Html.Raw` sur du contenu utilisateur (Label/RecipientName/adresse) — encodage Razor par défaut partout. Confirmations de suppression (adresse, commande) attachées via `<script nonce="@CspNonce.Nonce">` (CSP sans `'unsafe-inline'`), jamais `onclick` inline — même motif que COMMERCE-OPERATIONS-001B/CLOSURE-1.

## 11. Accessibilité et responsive

- Formulaires : `<label>` associé à chaque champ, `<fieldset>`/`<legend>` sur le formulaire d'adresse et la sélection d'adresse au checkout, messages de validation reliés (`asp-validation-for`).
- Nav compte : landmark `<nav aria-label>`, `aria-current="page"` sur l'onglet actif, cible tactile suffisante (classes `nav-link`/`btn` Bootstrap standard du site).
- Historique de commandes : cartes empilées (`list-group`), jamais de `<table>` avec défilement horizontal.
- Testé visuellement aux largeurs 320/375/390/430/768/1024/1280/1440 via la grille Bootstrap déjà en place sur le site (`col-12 col-md-3`/`col-12 col-md-9` pour nav+contenu, `col-12 col-sm-6`/`col-12 col-lg-6` pour les cartes d'adresse) — aucun nouveau breakpoint personnalisé introduit.

## 12. Migration

`AddCustomerAddresses` (CosmechicsContext uniquement) : crée `CustomerAddresses` + ses deux index. Aucune modification d'Identity, aucune modification financière/Stripe, aucun `DROP` historique. `Up()`/`Down()` lus intégralement — `Down()` supprime simplement la table.

```
MIGRATIONS_CREATED=AddCustomerAddresses (CosmechicsContext)
DATABASE_RECONSTRUCTIBLE=OUI (validé sur SQL Server 2022 jetable : reconstruction complète ApplicationDbContext puis CosmechicsContext depuis une base vide)
MODEL_MIGRATION_DRIFT=NONE (dotnet ef migrations has-pending-model-changes, ApplicationDbContext ET CosmechicsContext)
```

## 13. Tests

| Fichier | Couverture |
|---|---|
| `AddressServiceTests.cs` | Création (première adresse auto-défaut, seconde non-défaut), promotion par défaut désactive l'ancienne, validation (champs requis, pays non-CA rejeté), liste scoping par utilisateur, ownership (get/update/delete/setDefault étrangers rejetés), suppression de l'adresse par défaut promeut une autre. |
| `AccountControllerTests.cs` | Dashboard owner/anonyme, profil (mise à jour valide, overposting sans effet), adresses (création, IDOR edit/delete/setDefault), commandes (liste scoping, pagination 12 commandes/2 pages), détail (owner/étranger), retours (liste scoping). |
| `CheckoutSavedAddressTests.cs` | Sélection d'adresse enregistrée → snapshot correct ; adresse étrangère → refusé, aucune commande créée ; adresse modifiée après commande → commande historique inchangée. |
| `AspNetUsersControllerAdminOnlyTests.cs` | Client refusé sur Index/Details/Edit (même sur son propre profil) ; Admin autorisé ; POST Admin avec overposting Identity-sensible sans effet. |
| `CancellationServiceTests.cs` (étendu) | `CanCancel` : éligible (pending impayée), inéligible (déjà annulée / expédiée / entièrement remboursée). |
| `SqlServerAddressTests.cs` (SQL Server réel) | Plusieurs adresses/une seule par défaut après reconstruction ; violation réelle de l'index unique filtré (contournement direct du service) ; concurrence réelle sur `SetDefaultAsync` (jamais deux défauts) ; client existant avec historique de commande peut ajouter plusieurs adresses sans affecter le snapshot historique. |

```
TESTS_BEFORE=240
TESTS_AFTER=282
TESTS_PASS=282
TESTS_FAIL=0
SQL_SERVER_TESTS=32/32 PASS (29 préexistants + 3 nouveaux fichiers/scénarios ci-dessus)
TEST_ARTIFACTS=0 (vérifié après chaque exécution, y compris SQL Server)
```

## 14. Gates finaux

```
BUILD=PASS (0 erreur, Release, --no-incremental)
WARNINGS_BEFORE=102 (baseline 0d68dfb)
WARNINGS_AFTER=96 (aucun code d'avertissement nouveau ; CS8602/CS8604 en baisse, résultat du refactor — pas de régression)
NEW_CODE_WARNINGS=0
NUGET_CRITICAL=0, NUGET_HIGH=0, NUGET_MODERATE=0, NUGET_LOW=0
SECRET_SCAN=CLEAN
```

## 15. Décisions métier non résolues (TODO_REQUIRES_BUSINESS_CONFIGURATION)

- `RETURN_WINDOW_DAYS` (déjà signalé en COMMERCE-OPERATIONS-001B, toujours non configuré — `IReturnService.CanRequestReturnAsync` ne fabrique aucune valeur)
- `REFUND_SHIPPING_POLICY`, `REFUND_TAX_POLICY`, `INVOICE_LEGAL_TAX_INFO` (déjà signalés en COMMERCE-OPERATIONS-001B)
- Politique d'anonymisation/rétention pour la suppression de compte d'un client ayant un historique de commande (§7)
- Portée de l'export de données personnelles (commandes/adresses/retours non inclus dans `DownloadPersonalData`, §7)

## 16. Hors périmètre (confirmé non traité)

Nouvel admin complet (au-delà du correctif de sécurité sur `AspNetUsersController`), carnet d'adresses de facturation distinct, coupons/promotions avancés, API transporteur, fournisseur de suivi automatisé, conformité facture légale complète, moteur PDF, SEO, audit A11Y complet au-delà des pages nouvelles de ce lot, refonte UX globale, DevOps/monitoring/analytics.
