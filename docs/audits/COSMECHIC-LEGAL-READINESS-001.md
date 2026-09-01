# COSMECHIC-LEGAL-READINESS-001 — Contraste de marque + gates de configuration juridique

Applique la décision PM sur la couleur de marque, puis traite les trois décisions
juridiques restantes (COSMETIC_OPENED_PRODUCT_RETURN_POLICY, DATA_RETENTION_PERIODS,
INVOICE_LEGAL_TAX_INFO) comme des **gates de configuration**, pas des sujets à trancher
techniquement. Rien n'est inventé.

## 1. Préflight

```
BASELINE_ATTENDUE=84291e2
HEAD (avant modification)=84291e2c3e7019df82c48cc1ee2bd2a772c8298e
WORKTREE=CLEAN
PUSH=aucun (branche jamais poussée sur origin, confirmé — aucune ref distante n'existe)
RESTORE=PASS / BUILD=PASS (0 erreur, 48 warnings) / TESTS (avant)=386/386 PASS
NUGET vulnérabilités=0 / EF drift=NONE (ApplicationDbContext + CosmechicsContext)
```

## 2. Décision PM — contraste de marque

```
BRAND_PRIMARY=#cb350b (approuvé, remplace #f4623a)
CONTRAST_ON_WHITE=5.18:1 (recalculé par test, pas codé en dur — voir section 8)
```

**Remplacement ciblé, pas aveugle.** 35 occurrences de `#f4623a` inventoriées une par une
dans `styles.css`/`design-system.css`, chacune classée par rôle réel :

| Rôle | Occurrences | Décision |
|---|---|---|
| Token racine (`--bs-primary`, `--bs-orange`, `--bs-link-color`) | 3 | **Remplacé** |
| Bouton primaire plein (`.btn-primary` : bg/border, y compris **disabled**) | 4 | **Remplacé** |
| Bouton primaire outline (`.btn-outline-primary` : texte, border, hover, active, **disabled**) | 9 | **Remplacé** |
| Case à cocher/radio cochée, curseur de plage (contrôles interactifs) | 6 | **Remplacé** |
| États actifs interactifs (dropdown ×2, nav-pills, pagination, list-group) | 6 | **Remplacé** |
| `.link-primary` (lien texte utilitaire) | 1 | **Remplacé** |
| Navigation `#mainNav` (survol/actif, y compris variante réduite) | 5 | **Remplacé** |
| Fallback `--cx-color-primary` (design-system.css) | 1 | **Remplacé** |
| **`--bs-progress-bar-bg`** (remplissage de barre de progression — information, pas texte/lien/bouton/contrôle) | 1 | **Conservé** (#f4623a) |
| **`hr.divider`** (ligne de séparation purement décorative) | 1 | **Conservé** (#f4623a) |

`--cx-color-primary-dark` (`#c34e2e`, utilisé pour `.cx-page-title`, texte de titre de
page) : **non touché** — déjà conforme AA avant ce lot (4.72:1 sur blanc), aucune raison
de le changer.

**Zones vérifiées** (toutes servies par le même `styles.css` partagé, aucune surcharge
locale trouvée par recherche — donc couvertes automatiquement) : admin
(`OrderOperations`, `Categories`, `Produits` CRUD), Identity (Login/Register/Manage),
Account, checkout (Cart/Summary), catalogue (Produits/Categories). Aucun style inline
`#f4623a` trouvé dans une vue `.cshtml` — confirmé par recherche exhaustive avant et après
le remplacement.

Contrastes recalculés (formule WCAG relative luminance, testée, pas affirmée) :
- Texte/lien `#cb350b` sur blanc : **5.18:1** (AA normal texte ≥4.5:1 : PASS)
- Texte blanc sur bouton `#cb350b` : **5.18:1** (PASS)
- `#cb350b` sur le fond de page `ghostwhite` du design-system : **4.90:1** (PASS)

## 3-9. Gates de configuration juridique — rien tranché sans preuve

Voir section 11 (matrice) pour le détail complet. Résumé :

```
COSMETIC_OPENED_PRODUCT_RETURN_POLICY=AWAITING_LEGAL_REVIEW (inchangé)
DATA_RETENTION_PERIODS=AWAITING_LEGAL_REVIEW (inchangé)
INVOICE_LEGAL_TAX_INFO=AWAITING_LEGAL_REVIEW (inchangé)
```

Marqueurs `TODO_REQUIRES_LEGAL_REVIEW` ajoutés/alignés (remplacent l'ancienne convention
`TODO_REQUIRES_BUSINESS_CONFIGURATION` là où le sujet est réellement juridique, pas
seulement une configuration métier) dans : `IReturnService.cs`, `ReturnService.cs`,
`BusinessInformationOptions.cs`, `Cart/Receipt.cshtml`.

## 4. Audit du reçu (facture)

`Cosmechic/Views/Cart/Receipt.cshtml` (route `GET /Cart/Receipt/{id}`, ownership
propriétaire-ou-admin déjà vérifiée) :

- **Aucune conformité fiscale revendiquée** : disclaimer explicite déjà présent
  ("Ce n'est pas une facture fiscale officielle"), reconfirmé par test.
- **Aucune donnée vendeur fictive** : `BusinessInformationOptions.LegalBusinessName`/
  `BusinessAddress`/`TaxRegistrationNumbers` ne sont référencés nulle part dans cette vue
  — ils n'apparaissent que si un jour réellement configurés (vides actuellement).
- **Aucun numéro TPS/TVQ inventé** : confirmé (aucune chaîne de ce type dans la vue).
- **Montants = snapshot persisté** : `Subtotal`/`ShippingAmount`/`TaxAmount`/
  `DiscountAmount`/`OrderTotal`/`RefundedAmount` tous lus directement depuis `OrderHeader`,
  jamais recalculés.
- **Remboursements n'altèrent jamais le snapshot** : `RefundedAmount` affiché comme une
  ligne de réduction séparée, `OrderTotal` reste la valeur d'origine — cohérent avec
  `ORDER_FINANCIAL_SNAPSHOT_IMMUTABLE=YES` (COSMECHIC-BUSINESS-POLICY-001).
- **Aucune donnée interne exposée** : ni `PaymentIntentId`, ni `StripeRefundId`, ni
  `IdempotencyKey`, ni `FailureCode`, ni `AdminComment` — confirmé par lecture du fichier
  et par test (`ReceiptInvoiceAuditTests.cs`).

Preuve : `ReceiptInvoiceAuditTests.cs` (5 tests) — disclaimer présent, aucune donnée
fiscale fabriquée, aucune donnée interne exposée, montants exacts du snapshot, accès
refusé pour un autre client (IDOR).

## 5. Retours produits cosmétiques ouverts

Architecture déjà en place (COSMECHIC-BUSINESS-POLICY-001) : `CommercePolicyOptions`
centralise les décisions commerciales/fiscales, `ReturnService.CanRequestReturnAsync` est
la source de vérité unique pour l'éligibilité. **Aucune règle spécifique aux produits
cosmétiques ouverts n'a été ajoutée** — ni "non retournable une fois ouvert", ni
"retournable sans condition", ni exception hygiène, ni délai spécial, ni frais de retour
propres à cette catégorie. Le jour où cette politique sera validée juridiquement, elle se
branchera au même endroit que `ReturnWindowDays` (un champ supplémentaire sur
`CommercePolicyOptions`, lu par `CanRequestReturnAsync`), sans toucher aux contrôleurs ou
aux vues — architecture déjà prouvée capable d'absorber une politique future sans la
disperser (précédent direct : RETURN_WINDOW_DAYS ajouté en BUSINESS-POLICY-001 exactement
de cette façon).

## 6. Conservation / anonymisation / export — recertification

- **FK intactes** : reconfirmé contre SQL Server réel (`AccountAnonymizationSqlServerTests.cs`,
  2 tests, ré-exécutés ce lot — 36 requêtes SQL réelles observées dans les logs de test,
  aucune régression).
- **Aucune suppression automatique par durée** : recherche exhaustive dans tout le dépôt
  (`IHostedService`, `BackgroundService`, `Timer`, `Quartz`, `Hangfire`, tâche planifiée) —
  **aucune correspondance**. Aucun job de purge temporelle n'existe, confirmé négativement.
- **Export cohérent avec ACCOUNT/BUSINESS-POLICY** : `PersonalDataExportTests.cs` (5 tests)
  ré-exécutés — IDOR toujours vérifié entre deux clients réels, aucune donnée Stripe
  interne (`StripeRefundId`/`IdempotencyKey`/`FailureCode`/`PaymentIntentId`), aucune
  `AdminComment`, aucune donnée d'un autre client.

## 7. Domaine / SEO — recertification

```
PRODUCTION_DOMAIN=https://cosmechic.ca (inchangé)
CANONICAL_BASE_URL=https://cosmechic.ca (inchangé)
WWW_STRATEGY=REDIRECT_TO_APEX (inchangé)
```

`SitemapAndCanonicalTests.cs` (12 tests) ré-exécutés : sitemap servi à `/sitemap.xml`
avec le bon type de contenu, jamais `localhost`/`127.0.0.1`/`http://` dans un `<loc>`,
jamais `www.` comme origine canonique, aucune route privée/admin/webhook listée ; balise
canonical jamais localhost/http/www ; redirection www→apex réelle (301 + `Location`
exact) ; hôte normal non affecté. Aucune modification nécessaire — déjà conforme.

## 8. Tests

Nouveaux : `BrandContrastTests.cs` (8, dont 2 calculent réellement le ratio WCAG plutôt
que de l'affirmer) et `ReceiptInvoiceAuditTests.cs` (5). Ré-exécutés sans modification :
`SitemapAndCanonicalTests.cs`, `PersonalDataExportTests.cs`,
`AccountAnonymizationSqlServerTests.cs`, `SqlServerReturnRefundPolicyTests.cs`,
`SqlServerRefundAndRestockConcurrencyTests.cs`, `ReturnWindowTests.cs` (30 jours,
inchangé) — tous verts, y compris contre SQL Server réel (Docker jetable, provisionné et
démonté proprement ce lot).

```
TESTS_BEFORE=386
TESTS_AFTER=399
TESTS_PASS=399
TESTS_FAIL=0
SQL_SERVER_VALIDATION=RÉEL (conteneur Docker jetable, 36+ requêtes SQL observées dans les
  logs de test contre AspNetUsers/OrderHeaders/CustomerAddresses/Refunds, aucune donnée
  résiduelle après démontage)
```

## 9. Gates finaux

```
RESTORE=PASS
BUILD=PASS (0 erreur)
WARNINGS_BEFORE=48 (31 diagnostics uniques par type)
WARNINGS_AFTER=48 (31 diagnostics uniques, identiques)
NEW_CODE_WARNINGS=0
NUGET_CRITICAL=0  NUGET_HIGH=0  NUGET_MODERATE=0  NUGET_LOW=0
MODEL_MIGRATION_DRIFT=NONE (ApplicationDbContext + CosmechicsContext — aucun changement
  de modèle ce lot, uniquement CSS/vues/commentaires)
SECRET_SCAN=CLEAN
TEST_ARTIFACTS=0
DOCKER_LEFTOVERS=0
OUT_OF_SCOPE_CHANGES=0
```

## 11. Matrice de revue juridique (obligatoire, section 3 de la directive)

| Clé | Emplacements exacts dans le code | Déjà implémenté | Dépend d'une décision juridique | Marqueur |
|---|---|---|---|---|
| **COSMETIC_OPENED_PRODUCT_RETURN_POLICY** | `Cosmechic/Services/CommercePolicyOptions.cs` (architecture prête à recevoir un champ) ; `Cosmechic/Services/ReturnService.cs` (`CanRequestReturnAsync`, gate technique existante : statut expédié/livré + fenêtre 30 jours, RIEN sur l'état d'ouverture) ; `Cosmechic/Services/IReturnService.cs` | Fenêtre de 30 jours (approuvée, BUSINESS-POLICY-001) et gate d'expédition/livraison | La règle spécifique "produit cosmétique ouvert" elle-même (retournable ou non, exception hygiène, délai/frais spécifiques) | `TODO_REQUIRES_LEGAL_REVIEW` dans `IReturnService.cs` et `ReturnService.cs` |
| **DATA_RETENTION_PERIODS** | `Cosmechic/Services/AccountAnonymizationService.cs` (anonymise, ne supprime jamais après un délai) ; `Cosmechic/Areas/Identity/Pages/Account/Manage/DeletePersonalData.cshtml(.cs)` (déclenchement à la demande du client uniquement, jamais automatique) | Anonymisation à la demande du client (approuvée, BUSINESS-POLICY-001), aucun hard-delete d'un compte avec historique | Durée légale de conservation avant purge/anonymisation éventuelle imposée par une obligation réglementaire (comptable, fiscale) plutôt qu'à la demande du client | Documenté dans `docs/audits/COSMECHIC-BUSINESS-POLICY-001.md` (`AWAITING_LEGAL_REVIEW`) ; aucun job de purge temporelle n'existe (recherche exhaustive, section 6) |
| **INVOICE_LEGAL_TAX_INFO** | `Cosmechic/Services/BusinessInformationOptions.cs` (`LegalBusinessName`/`BusinessAddress`/`TaxRegistrationNumbers`, vides par défaut) ; `Cosmechic/Views/Cart/Receipt.cshtml` (reçu, disclaimer explicite "pas une facture fiscale officielle") | Reçu de commande fonctionnel avec snapshot financier correct, disclaimer explicite | Quelles informations légales/fiscales sont réellement obligatoires sur une facture pour cette juridiction, et leurs valeurs réelles | `TODO_REQUIRES_LEGAL_REVIEW` dans `BusinessInformationOptions.cs` et `Cart/Receipt.cshtml` |

Aucune affirmation juridique catégorique n'a été introduite ou modifiée pour ces trois
clés — seule la terminologie des marqueurs a été alignée sur
`TODO_REQUIRES_LEGAL_REVIEW`, sans changer la substance déjà correcte héritée des lots
précédents.

## 12. Diff review

| FILE | CHANGE | REASON |
|---|---|---|
| `Cosmechic/wwwroot/css/styles.css` | 33 valeurs `#f4623a`→`#cb350b` (2 conservées intentionnellement) | Décision PM contraste |
| `Cosmechic/wwwroot/css/design-system.css` | 2 valeurs + commentaire mis à jour | idem |
| `Cosmechic/Services/BusinessInformationOptions.cs` | Commentaire, marqueur `TODO_REQUIRES_LEGAL_REVIEW` | INVOICE_LEGAL_TAX_INFO |
| `Cosmechic/Views/Cart/Receipt.cshtml` | Commentaire, marqueur `TODO_REQUIRES_LEGAL_REVIEW` | idem |
| `Cosmechic/Services/IReturnService.cs` | Commentaire corrigé (fenêtre 30j n'est plus "non codée dur"), marqueur ajouté | COSMETIC_OPENED_PRODUCT_RETURN_POLICY |
| `Cosmechic/Services/ReturnService.cs` | Marqueur aligné | idem |
| `Cosmechic.Tests/BrandContrastTests.cs` (nouveau) | +170 | Preuve contraste |
| `Cosmechic.Tests/ReceiptInvoiceAuditTests.cs` (nouveau) | +80 | Preuve reçu |

```
OUT_OF_SCOPE_CHANGES=0
```
Aucun fichier non justifiable. Aucune valeur légale/fiscale/de rétention inventée.
