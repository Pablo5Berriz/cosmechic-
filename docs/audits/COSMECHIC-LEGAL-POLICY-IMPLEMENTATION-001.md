# COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001

Implémente la politique commerciale de retour validée par le PM
(COSMECHIC-LEGAL-DECISION-RESEARCH-001), corrige les deux défauts de confidentialité
confirmés (AspNetUsers legacy address, ShoppingCart), et prépare — sans jamais inventer de
valeur — le scaffold de configuration pour les informations fiscales vendeur. Ce lot fait
suite à COSMECHIC-BUSINESS-POLICY-001, COSMECHIC-LEGAL-READINESS-001,
COSMECHIC-LEGAL-DECISIONS-PREFLIGHT-001 et COSMECHIC-LEGAL-DECISION-RESEARCH-001.

**BASELINE_SHA** : `62e9811e09299d61df7fe38fc696a85fd78351f0`
**FINAL_SHA** : voir commit ci-dessous (un seul commit local pour ce lot)

---

## 1. Préflight et recertification

| Vérification | Résultat |
|---|---|
| HEAD avant travaux | `62e9811` — conforme |
| Worktree | CLEAN |
| Restore | PASS |
| Build (méthode clean+no-incremental+sln, référence établie au lot précédent) | PASS — 0 erreur, 48 avertissements MSBuild / 46 fingerprints uniques (baseline exactement reproduite) |
| Tests avant modification | 399/399 PASS |
| Vulnérabilités NuGet | 0 |
| Dérive EF (`CosmechicsContext`, `ApplicationDbContext`) | NONE |
| Conteneurs Docker résiduels | Aucun |
| Artefacts de test résiduels | Aucun |

Docker/SQL Server réel démarré avec succès pour ce lot (requis section 18) — aucun
`BLOCKED_ENVIRONMENT`.

---

## 2. Décision métier appliquée

```
RETURN_POLICY_CHANGE_OF_MIND:
- Produit non ouvert, non utilisé et revendable : retour admissible dans la fenêtre
  commerciale de 30 jours calendaires.
- Produit ouvert, descellé ou utilisé : retour pour changement d'avis refusé.
- Exception : cette restriction commerciale ne limite jamais les recours légaux
  applicables (produit défectueux, non conforme, dangereux, mauvais produit expédié,
  faute du marchand, autres droits légaux).
- Réaction indésirable/sécurité alléguée : jamais traitée comme changement d'avis —
  dirigée vers une voie de triage/escalade distincte.
```

---

## 3. Modèle : avant / après

### Avant
- `ReturnItem` : `Id, ReturnRequestId, OrderDetailId, Quantity, Reason (texte libre), Restocked, RestockedAt`.
- `IReturnService.CanRequestReturnAsync(OrderDetail, int)` : une seule règle — fenêtre de 30 jours appliquée uniformément à **toute** demande de retour, sans distinction de motif.
- `RefundOrchestrationService.RequestReturnRefundAsync(returnRequestId, RefundCause cause, ...)` : `cause` fourni par l'admin via un `<select>` fermé au moment du remboursement — aucun lien structurel avec la demande de retour initiale.
- `ReturnRequest.Status` : `Requested → Approved/Rejected → Received → Completed`.

### Après
- `ReturnReasonCategory` (nouvel enum fermé) : `ChangeOfMind, DefectOrNonConformity, WrongItemOrMerchantFault, SafetyOrAdverseReaction, LegacyUnclassified`.
- `ReturnItem` gagne : `Category (ReturnReasonCategory, requis), IsOpened (bool?), IsUsed (bool?), CustomerDeclaredResellable (bool?)` — par **ligne**, pas par demande (cohérent avec l'architecture déjà per-`OrderDetail` de `CanRequestReturnAsync`).
- `IReturnService.CanRequestReturnAsync(OrderDetail, int, ReturnReasonCategory?, bool?, bool?, bool?)` : la fenêtre de 30 jours et les déclarations d'état ne s'appliquent qu'à `ChangeOfMind` ; les autres catégories restent soumises aux portes de base inconditionnelles (commande expédiée/livrée, paiement confirmé, quantité disponible) mais jamais à la fenêtre ni à la restriction "produit ouvert".
- `RefundOrchestrationService.RequestReturnRefundAsync(returnRequestId, reason, ...)` : `cause` n'est plus un paramètre — dérivée exclusivement des `Category` des lignes du retour, déjà persistées et validées à la création.
- `ReturnRequest.Status` gagne `NeedsSafetyReview` : `Requested → {Approved, Rejected}`, `NeedsSafetyReview → {Requested}` (seule sortie, admin-only), `Approved → Received → Completed`.
- `IReturnService.ReleaseSafetyReviewAsync` (nouveau) : seule action capable de sortir une demande de `NeedsSafetyReview`.

### Routage des raisons (tableau)

| Category | Fenêtre 30 jours | Déclarations requises | Statut initial | Cause financière dérivée |
|---|---|---|---|---|
| `ChangeOfMind` | OUI | `IsOpened=false, IsUsed=false, CustomerDeclaredResellable=true` obligatoires | `Requested` | `CustomerRemorse` |
| `DefectOrNonConformity` | NON | Ignorées | `Requested` | `MerchantFault` |
| `WrongItemOrMerchantFault` | NON | Ignorées | `Requested` | `MerchantFault` |
| `SafetyOrAdverseReaction` | NON | Ignorées | `NeedsSafetyReview` | `MerchantFault` (favorable au client tant qu'une allégation de sécurité n'est pas écartée) |
| `LegacyUnclassified` (backfill seulement) | N/A | N/A | N/A — jamais créée par l'application | `CustomerRemorse` (traitement prudent par défaut) |

### Distinction commercial / garantie légale / sécurité

- **Politique commerciale volontaire** : uniquement `ChangeOfMind` — seule catégorie soumise à la fenêtre de 30 jours et à la restriction produit ouvert/utilisé.
- **Garantie légale (jamais limitée par la politique commerciale)** : `DefectOrNonConformity`, `WrongItemOrMerchantFault` — aucune fenêtre, aucune restriction d'état imposée par ce code (aucune période de garantie légale n'est inventée non plus : les portes de base — commande expédiée/payée — restent seules à s'appliquer).
- **Sécurité (jamais un retour ordinaire)** : `SafetyOrAdverseReaction` — route obligatoirement vers `NeedsSafetyReview`, jamais d'auto-approbation ni d'auto-remboursement.

---

## 4. Migration et backfill

**Migration** : `20260902013002_AddReturnReasonCategory` (additive uniquement — `AddColumn` sur `ReturnItems` : `Category nvarchar(50) NOT NULL`, `IsOpened bit NULL`, `IsUsed bit NULL`, `CustomerDeclaredResellable bit NULL`). Aucune perte de données, aucune colonne supprimée.

**Défaut réel corrigé pendant la génération** : EF Core génère par défaut `defaultValue: ""` pour l'`AddColumn` d'une colonne string `NOT NULL` sans configuration de modèle explicite — une chaîne vide n'est pas une valeur `ReturnReasonCategory` valide et aurait rompu la désérialisation de toute ligne `ReturnItem` historique dès le premier chargement après migration. Corrigé manuellement en `defaultValue: "LegacyUnclassified"`.

**Défaut EF permanent délibérément absent du modèle** : `CosmechicsContext.cs` ne configure **aucun** `.HasDefaultValue()` sur `Category`. Raison, confirmée par un avertissement EF Core explicite obtenu lors d'une première tentative : `ReturnReasonCategory.ChangeOfMind` vaut `0`, le sentinel CLR par défaut de l'enum — un défaut de modèle permanent aurait fait qu'EF **substitue silencieusement `LegacyUnclassified` à chaque insertion où `Category` est explicitement `ChangeOfMind`**, un défaut réel qui aurait cassé la politique commerciale elle-même dès le premier retour "changement d'avis" créé. Le défaut `LegacyUnclassified` n'existe donc que dans l'opération `AddColumn` de la migration (backfill ponctuel des lignes déjà en base), jamais comme configuration de modèle.

**Preuve du backfill** (`HistoricalReturnItem_InsertedWithoutCategory_BackfillsToLegacyUnclassified_NeverChangeOfMind`, SQL Server réel) : insertion d'une ligne `ReturnItem` sans spécifier `Category` (reproduisant exactement l'état d'une ligne pré-existante au moment de l'exécution de l'`AddColumn`), relecture via EF, confirmation que la valeur backfillée est `LegacyUnclassified` — jamais `ChangeOfMind` (`0`).

**Après migration** : reconstruction complète de la base (SqlServerFixture, conteneur jetable), `has-pending-model-changes` = NONE pour les deux contextes.

---

## 5. Corrections de confidentialité

### 5.1 AspNetUsers — champs legacy (`StreetAddress/City/State/PostalCode`)

Confirmé par le lot de recherche : propriétés CLR réelles de `Cosmechic.Models.AspNetUser`
(`CosmechicsContext`), actives via `AspNetUsersController` (admin CRUD) — jamais des
propriétés fantômes.

- **Anonymisation** (`AccountAnonymizationService.AnonymizeAsync`) : les 4 champs sont
  maintenant explicitement mis à `null` pour le compte anonymisé, au même titre que les
  champs `OrderHeader` déjà couverts.
- **Export personnel** (`DownloadPersonalData.OnPostAsync`) : un nouveau bloc `LegacyProfile`
  interroge directement `CosmechicsContext.AspNetUsers` (jamais visible via la réflexion
  `[PersonalDataAttribute]` sur `IdentityUser`, qui ne peut structurellement pas voir des
  propriétés absentes du type `IdentityUser`) et l'inclut dans l'export tant que ces champs
  existent réellement dans le modèle actif. Aucun second modèle concurrent créé.
- **Preuve SQL Server réelle** :
  `AnonymizeAsync_ClearsLegacyAspNetUserFields_DeletesOwnCart_PreservesOtherAccountCartAndOrders`.

### 5.2 ShoppingCart — cycle de vie de confidentialité

Confirmé : aucun panier anonyme ne peut exister (le seul point d'écriture est
`[Authorize]`) ; aucun panier "historique" distinct (vidé intégralement à la finalisation
d'une commande) ; les paniers abandonnés/orphelins n'étaient jamais couverts par
l'anonymisation.

- **Anonymisation** : `AccountAnonymizationService.AnonymizeAsync` supprime maintenant
  intégralement (`RemoveRange`, jamais une simple désassociation) toutes les lignes
  `ShoppingCart` du compte — un panier n'est pas un enregistrement comptable, contrairement à
  `OrderHeader`/`OrderDetail`, jamais touchés.
- **Preuve SQL Server réelle** (même test que 5.1) : panier du compte anonymisé supprimé,
  panier d'un **autre** compte intact, commande historique intacte avec sa FK valide.

---

## 6. Rétention des données — architecture, sans durée codée

Aucune durée n'est fixée dans ce lot. Aucun scheduler, cron ou job créé. Aucune donnée
supprimée pour cause d'ancienneté. Classification par catégorie, cible architecturale
uniquement :

| Catégorie | Cible architecturale | Statut dans ce lot |
|---|---|---|
| Commandes / détails financiers / taxes / remboursements | 6 ans après la fin de l'année fiscale concernée (baseline Revenu Québec validée par le PM) | Non implémenté — nécessite `TODO_REQUIRES_BUSINESS_CONFIGURATION` pour la date de fin d'exercice réelle de Cosmechic, non connue de ce dépôt. Aucune commande ni donnée fiscale supprimée. |
| Données liées à un litige/opposition/vérification en cours | Conservation prolongée jusqu'à fermeture | Non implémenté — aucun mécanisme de détection d'un litige en cours n'existe dans ce dépôt. |
| Adresse historique sur une commande (`OrderHeader`) | Uniquement dans la mesure nécessaire au dossier transactionnel | Déjà l'architecture actuelle (anonymisation partielle sur demande, City/State/PostalCode conservés pour l'audit fiscal). |
| Profil actif (`AspNetUsers`) | Durée du compte | Déjà l'architecture actuelle (anonymisation sur demande explicite du client). |
| Adresses sauvegardées hors historique (`CustomerAddress`) | Suppression/anonymisation à la fermeture de compte | Déjà en place (suppression intégrale à l'anonymisation). |
| Champs legacy AspNetUsers | Anonymisation obligatoire à la fermeture du compte | **Implémenté dans ce lot** (section 5.1). |
| Panier abandonné authentifié (`ShoppingCart`) | Durée opérationnelle courte à fixer (non décidée) | Suppression à l'anonymisation implémentée (section 5.2) ; aucune purge par ancienneté avant anonymisation — décision de durée opérationnelle non prise, aucun scheduler créé. |
| Panier lié à un compte anonymisé | Suppression ou désassociation | **Implémenté dans ce lot** : suppression (section 5.2). |
| Retours/remboursements liés à une transaction | Alignement avec la conservation transactionnelle | Suit `OrderHeader`/`Refund` — non modifié, déjà jamais supprimé. |
| Logs de sécurité | Politique distincte, minimale, à documenter | Hors périmètre applicatif — aucun sink persistant dans ce dépôt (confirmé au lot de recherche). |
| Formulaire de contact | Politique distincte, plus courte que 6 ans sauf dossier actif | Hors périmètre applicatif — rien n'est persisté par ce code (courriel uniquement). |

`GLOBAL_BLANKET_RETENTION` : **NON codé**, conformément à l'instruction explicite de ne
jamais créer un `RETENTION_DAYS` global unique.

---

## 7. Facture / reçu — scaffold uniquement, aucune donnée fabriquée

`BusinessInformationOptions` étendu (remplace `TaxRegistrationNumbers`, chaîne générique
jamais consommée par aucune vue, par les identifiants structurés exacts nommés par le PM) :

```
LegalBusinessName    (string?, vide)
TradeName             (string?, vide)   [nouveau]
BusinessAddress       (string?, vide)
GstRegistrationStatus (TaxRegistrationStatus, Unknown)  [nouveau]
GstNumber             (string?, vide)                    [nouveau]
QstRegistrationStatus (TaxRegistrationStatus, Unknown)  [nouveau]
QstNumber             (string?, vide)                    [nouveau]
```

`TaxRegistrationStatus { Unknown, NotRegistered, Registered }` — `Unknown` est le seul défaut
honnête : "non configuré" n'est ni "inscrit" ni "non inscrit", les deux étant des
affirmations factuelles que ce dépôt n'a jamais été autorisé à faire.

**Aucune vue ne consomme ces nouveaux champs.** `Cart/Receipt.cshtml` reste exact et
fonctionnel, inchangé fonctionnellement (seul le commentaire expliquant l'absence de données
fiscales a été mis à jour pour refléter les noms de champs réels). Aucun placeholder, aucun
faux numéro, aucun faux statut affiché. `LEGAL_BUSINESS_IDENTIFIERS_CONFIGURED=NO`,
`FAKE_TAX_DATA_CREATED=NO`.

---

## 8. Interface

### Client (`Views/Returns/Request.cshtml`)
- Sélecteur de raison structuré par ligne (`<select>`, 4 catégories réelles — jamais
  `LegacyUnclassified`, réservée au backfill).
- Champ de détails texte libre, complément narratif uniquement.
- Fieldset/legend "Déclaration du produit" (3 cases à cocher : ouvert / utilisé / revendable
  déclaré) — toujours soumis, appliqué côté serveur uniquement pour `ChangeOfMind` (amélioration
  progressive JS pour l'affichage/masquage, jamais un contrôle de sécurité — la validation
  reste entièrement `ReturnService.CanRequestReturnAsync`).
- Note explicative prudente et factuelle sur la portée de la politique et l'exception légale.
- Note explicite indiquant qu'une demande de sécurité suit un processus distinct.
- Accessibilité : labels réels, fieldset/legend, pas de couleur seule comme indicateur.

### Admin (`Views/OrderOperations/Details.cshtml`)
- État `NeedsSafetyReview` affiché distinctement avec bouton "Libérer vers la file normale"
  (POST, antiforgery, commentaire de revue optionnel).
- Colonne motif par ligne affichant `Category` et, pour `ChangeOfMind`, les déclarations.
- Formulaire de remboursement de retour : `Cause` retiré — plus aucune décision admin sur ce
  point, calculée entièrement côté serveur.

---

## 9. Contraintes d'autorisation préservées

- `ReleaseSafetyReview` hérite `[Authorize(Roles = "Admin")]` du contrôleur (comme toutes les
  autres actions post-achat) — POST + antiforgery.
- Ownership client inchangée (`ReturnsController` dérive toujours `userId` du serveur, jamais
  du formulaire).
- DTOs étroits : `ReturnItemFormInput` ne porte que `OrderDetailId, Quantity, Reason, Category,
  IsOpened, IsUsed, CustomerDeclaredResellable` — aucun champ financier, aucun champ de
  workflow interne (`Status`, `Restocked`, `RestockedAt`) bindable par le client.
- `TriggerReturnRefundInput` : `Cause` retiré, aucune donnée navigateur ne peut plus influencer
  la classification financière du remboursement.
- Toutes les décisions serveur sont rechargées depuis la DB à chaque appel (aucun état de
  confiance transmis par le client).

---

## 10. Tests (matrice complète, directive section 17)

| Section | Couverture | Fichier(s) |
|---|---|---|
| A. ChangeOfMind | Jour 0/29/30/31 (fenêtre), opened=true refusé, used=true refusé, resellable=false refusé, combinaison valide acceptée, champs omis refusés, overposting sans effet hors DTO | `ReturnWindowTests.cs`, `ReturnPolicyImplementationTests.cs` |
| B. Defect/NonConformity | >30 jours non rejeté par la fenêtre seule, état ouvert/utilisé non rejeté automatiquement | `ReturnWindowTests.cs`, `ReturnPolicyImplementationTests.cs` |
| C. WrongItem/MerchantFault | >30 jours non rejeté, opened=true non rejeté, classification connue dès la demande, navigateur ne peut pas forcer la cause finale, remboursement conforme | `ReturnPolicyImplementationTests.cs`, `SqlServerReturnRefundPolicyTests.cs` |
| D. Safety/Adverse Reaction | Création possible hors ChangeOfMind, produit ouvert n'entraîne pas auto-rejet, routage NeedsSafetyReview, client ne peut pas auto-approuver/rembourser, action admin protégée, audit écrit | `ReturnPolicyImplementationTests.cs`, `PostPurchaseAuthorizationTests.cs` |
| E. Privacy AspNetUsers | Anonymisation nettoie les champs legacy, export les inclut avant anonymisation, aucun autre utilisateur exposé | `AccountAnonymizationSqlServerTests.cs` (SQL Server réel) |
| F. ShoppingCart | Anonymisation supprime le panier du compte, panier d'un autre compte intact, commandes historiques intactes, FK intacte | `AccountAnonymizationSqlServerTests.cs` (SQL Server réel) |
| G. Finances (régression) | Refund shipping/taxes/partiel/concurrent, stock, restock, snapshot immuable, convergence webhook/idempotence | `SqlServerReturnRefundPolicyTests.cs`, `SqlServerRefundAndRestockConcurrencyTests.cs` (préexistants, tous verts après migration) |
| Migration historique | Backfill LegacyUnclassified prouvé sur SQL Server réel, jamais requalifié ChangeOfMind | `SqlServerReturnRefundPolicyTests.cs` |

**Résultat** : 420/420 tests PASS (399 préexistants + 21 nouveaux), y compris l'intégralité de
la suite SQL Server réelle (base reconstruite à zéro, migrations Identity puis Cosmechics
appliquées).

---

## 11. Diff review (fichiers modifiés/ajoutés, tous IN_SCOPE)

| Fichier | Raison |
|---|---|
| `Cosmechic.Utility/SD.cs` | Nouvelle constante `ReturnStatusNeedsSafetyReview` |
| `Cosmechic/Services/ReturnReasonCategory.cs` (nouveau) | Enum fermé, source de vérité du motif |
| `Cosmechic/Models/ReturnItem.cs` | Category/IsOpened/IsUsed/CustomerDeclaredResellable |
| `Cosmechic/Models/CosmechicsContext.cs` | Configuration EF des nouvelles colonnes |
| `Cosmechic/Models/ViewModels/CreateReturnRequestInput.cs` | DTO étendu, toujours étroit |
| `Cosmechic/Services/IReturnService.cs` / `ReturnService.cs` | Éligibilité scopée par catégorie, routage sécurité, `ReleaseSafetyReviewAsync` |
| `Cosmechic/Controllers/ReturnsController.cs` | Sites d'appel mis à jour |
| `Cosmechic/Services/IRefundOrchestrationService.cs` / `RefundOrchestrationService.cs` | Cause dérivée serveur |
| `Cosmechic/Models/ViewModels/OrderOperationsInputs.cs` | `Cause` retiré de `TriggerReturnRefundInput` |
| `Cosmechic/Controllers/OrderOperationsController.cs` | `TriggerReturnRefund` mis à jour, `ReleaseSafetyReview` ajouté |
| `Cosmechic/Views/Returns/Request.cshtml` | UI motif structuré + déclarations |
| `Cosmechic/Views/OrderOperations/Details.cshtml` | UI NeedsSafetyReview, retrait du select Cause |
| `Cosmechic/Services/AccountAnonymizationService.cs` | Fuites AspNetUsers/ShoppingCart corrigées |
| `Cosmechic/Areas/Identity/Pages/Account/Manage/DownloadPersonalData.cshtml.cs` | Export legacy profile |
| `Cosmechic/Services/BusinessInformationOptions.cs` | Scaffold identifiants fiscaux (aucune valeur) |
| `Cosmechic/appsettings.json` | Nouvelles clés de config, vides/Unknown |
| `Cosmechic/Views/Cart/Receipt.cshtml` | Commentaire seulement (noms de champs à jour) |
| `Cosmechic/Migrations/20260902013002_AddReturnReasonCategory.*`, `CosmechicsContextModelSnapshot.cs` | Migration additive |
| `Cosmechic.Tests/*.cs` | Mise à jour des sites d'appel existants + nouveaux tests (matrice section 10 ci-dessus) |

`OUT_OF_SCOPE_CHANGES = 0`.

---

## 12. Limites et éléments juridiques encore non clos

- `LEGAL_BUSINESS_IDENTIFIERS_CONFIGURED = NO` — nom légal, adresse, statuts et numéros
  TPS/TVQ toujours à fournir par le PM avant toute implémentation de facture fiscale
  conforme.
- Durée de rétention transactionnelle (6 ans) non codée en mécanisme — la date de fin
  d'exercice fiscale réelle de Cosmechic reste inconnue de ce dépôt
  (`TODO_REQUIRES_BUSINESS_CONFIGURATION`).
- Durée opérationnelle du panier abandonné (hors anonymisation) non décidée — aucun
  mécanisme de purge par ancienneté créé.
- Obligation de signalement sous 2 jours (LCSPC, identifiée au lot de recherche) pour un
  incident de sécurité produit **non automatisée** — `NeedsSafetyReview` fournit uniquement
  le triage interne, pas la conformité réglementaire externe complète. Marqué
  `TODO_REQUIRES_LEGAL_OPERATIONAL_CONFIGURATION` dans l'intention, à documenter
  explicitement si un incident survient.
- Formulation exacte de divulgation pré-achat (art. 54.4 LPC, identifié au lot de recherche)
  non revue par un juriste.
- Aucune revue juridique humaine finale n'a eu lieu sur ce lot.

**`LEGAL_READINESS` ne passe PAS à `PASS`** du simple fait que ce lot est techniquement
réussi — voir rapport final.

---

## 13. Rapport final

```
LOT=COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001
STATUS=PASS

BASELINE_SHA=62e9811e09299d61df7fe38fc696a85fd78351f0
FINAL_SHA=(voir commit)

RETURN_REASON_MODEL=ReturnReasonCategory{ChangeOfMind,DefectOrNonConformity,WrongItemOrMerchantFault,SafetyOrAdverseReaction,LegacyUnclassified}
CHANGE_OF_MIND_POLICY=30_DAYS_NON_OPENED_NON_USED_RESELLABLE
RETURN_WINDOW_SCOPE=CHANGE_OF_MIND_ONLY
OPENED_CHANGE_OF_MIND=REJECTED
USED_CHANGE_OF_MIND=REJECTED
NON_RESELLABLE_CHANGE_OF_MIND=REJECTED

DEFECT_NONCONFORMITY_PATH=NO_WINDOW_NO_CONDITION_RESTRICTION_BASE_GATES_ONLY
MERCHANT_FAULT_PATH=NO_WINDOW_NO_CONDITION_RESTRICTION_KNOWN_AT_REQUEST_TIME
SAFETY_ADVERSE_REACTION_PATH=ROUTES_TO_NEEDS_SAFETY_REVIEW_NEVER_AUTO_PROCESSED
SAFETY_TRIAGE=NeedsSafetyReview_status_admin_only_release_to_Requested

CUSTOMER_CONDITION_DECLARATION=IsOpened_IsUsed_CustomerDeclaredResellable_per_ReturnItem_nullable
FREE_TEXT_REASON_SOURCE_OF_TRUTH=NO

REFUND_SHIPPING_POLICY_REGRESSION=PASS
REFUND_TAX_POLICY_REGRESSION=PASS
FINANCIAL_SNAPSHOT_IMMUTABLE=PASS

ASPNETUSERS_LEGACY_ADDRESS_PRIVACY_FIX=DONE
SHOPPING_CART_PRIVACY_FIX=DONE
PERSONAL_DATA_EXPORT_UPDATED=YES
ACCOUNT_ANONYMIZATION_UPDATED=YES

RETENTION_ARCHITECTURE=DOCUMENTED_PER_CATEGORY_NO_DURATIONS_CODED
QUEBEC_TAX_RETENTION_BASELINE=6_YEARS
GLOBAL_BLANKET_RETENTION=NO

SELLER_LEGAL_OPTIONS=BusinessInformationOptions_extended_TradeName_GstRegistrationStatus_GstNumber_QstRegistrationStatus_QstNumber
LEGAL_BUSINESS_IDENTIFIERS_CONFIGURED=NO
FAKE_TAX_DATA_CREATED=NO

MIGRATIONS_CREATED=1 (AddReturnReasonCategory, CosmechicsContext)
DATABASE_RECONSTRUCTIBLE=YES
MODEL_MIGRATION_DRIFT=NONE

SQL_SERVER_VALIDATION=PASS (base reconstruite à zéro, 41+ tests SQL Server réels verts, backfill historique prouvé)
TESTS_BEFORE=399
TESTS_AFTER=420
TESTS_PASS=420
TESTS_FAIL=0

WARNINGS_BEFORE=48 (MSBuild) / 46 (fingerprints uniques)
WARNINGS_AFTER=48 (MSBuild) / 46 (fingerprints uniques)
NEW_CODE_WARNINGS=0 (diff de fingerprints : un seul décalage de numéro de ligne sur un avertissement préexistant dans un fichier modifié, aucun nouveau diagnostic)

NUGET_CRITICAL=0
NUGET_HIGH=0
NUGET_MODERATE=0
NUGET_LOW=0

SECRET_SCAN=CLEAN
TEST_ARTIFACTS=0
DOCKER_LEFTOVERS=0
OUT_OF_SCOPE_CHANGES=0

LEGAL_REVIEW_REQUIRED=YES
PRODUCTION_RELEASE_AUTHORIZATION=BLOCKED

PRODUCTION_TOUCHED=NO
REAL_STRIPE_USED=NO
REAL_EMAIL_SENT=NO

COMMIT=(voir ci-dessous)
PUSHED=NO

COSMECHIC_LEGAL_POLICY_IMPLEMENTATION_001=PASS
SAFE_TO_START_NEXT_LOT=NO
```

**STOP.** Aucun autre lot ne commence. En attente de validation PM.
