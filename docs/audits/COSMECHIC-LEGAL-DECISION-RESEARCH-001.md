# COSMECHIC-LEGAL-DECISION-RESEARCH-001

**MODE : RESEARCH_AND_RECERTIFICATION. IMPLEMENTATION=FORBIDDEN.** Ce lot ferme les deux
incertitudes de preuve soulevées par le PM sur COSMECHIC-LEGAL-DECISIONS-PREFLIGHT-001
(écart d'avertissements 48 vs 46, gaps de confidentialité AspNetUsers/ShoppingCart), puis
mène une recherche juridique sourcée sur les trois décisions encore ouvertes. Aucune règle,
aucune durée, aucun numéro fiscal n'est codé ni inventé. `CODE_CHANGES = NONE` (hors ce
document).

---

## Section 1 — Préflight strict

| Vérification | Résultat |
|---|---|
| Branche | `claude/cosmechic-full-audit-0up8zj` |
| HEAD | `6fab329924a71e6fc98b3af1eb90d05af7f8059e` |
| `EXPECTED_HEAD` | `6fab329` — **conforme** |
| `git status --short` | vide — **WORKTREE=CLEAN** |
| `git log -3 --oneline` | `6fab329` docs(legal) → `e1c4770` feat(design) → `84291e2` feat(commerce) |

Aucune divergence. Le lot procède.

---

## Section 2 — Réconciliation de l'écart d'avertissements 48 → 46

### 2.1 Méthode

Le code applicatif est strictement identique entre `e1c4770` et `6fab329` (seul un fichier
`docs/audits/*.md` a été ajouté par le commit `6fab329` — aucun `.cs`/`.cshtml` modifié).
L'hypothèse à vérifier n'est donc pas "le code a changé", mais "la méthode de build a changé".

Méthode strictement identique appliquée aux deux commits, dans un **worktree Git isolé** pour
`e1c4770` (`/tmp/.../wt-e1c4770`, supprimé après usage) :

```
dotnet clean Cosmechic.sln -c Release
dotnet restore Cosmechic.sln
dotnet build Cosmechic.sln -c Release --no-incremental
```

### 2.2 Résultats bruts

| Mesure | `e1c4770` (worktree isolé) | `6fab329` (HEAD courant) |
|---|---|---|
| Résumé MSBuild (`N Warning(s)`) | **48** | **48** |
| Erreurs | 0 | 0 |
| Fingerprints uniques (`fichier\|ligne\|colonne\|code\|message`) | **46** | **46** |
| `diff` des deux ensembles de fingerprints | — | **IDENTIQUE, AUCUNE DIFFÉRENCE** |

**Preuve directe** : avec une méthode de build strictement identique (solution complète,
`--no-incremental`, après `dotnet clean`), **`e1c4770` produit exactement 48 avertissements
MSBuild, tout comme `6fab329`.** Le nombre "46" du rapport précédent (LEGAL-DECISIONS-
PREFLIGHT-001) provenait d'une invocation différente (`dotnet build -c Release` sans `--no-
incremental`, immédiatement après un `dotnet restore` dans le même conteneur) — un mode de
build incrémental, pas une différence de code.

### 2.3 Explication du mécanisme exact (transparence complète)

En examinant les lignes brutes du journal de build (avant dédoublonnage), chaque diagnostic
apparaît **exactement deux fois** dans la console (une fois pendant la compilation, une fois
dans le récapitulatif final `Build succeeded` — comportement standard et documenté du logger
console de `dotnet build`/MSBuild, sans rapport avec le code). Sur les 46 diagnostics uniques,
**2 sont émis deux fois par le compilateur Roslyn lui-même** au même endroit exact
(`Controllers/ProduitsController.cs:696:26` et `Controllers/CartController.cs:32:26`, tous
deux `CS8602`) — un comportement connu de l'analyse de flux "nullable" de Roslyn sur certaines
branches de contrôle. Le calcul se vérifie exactement :

```
44 diagnostics uniques émis une fois + 2 diagnostics uniques émis deux fois par Roslyn
  = 44 + (2 × 2) = 48   ← correspond exactement au résumé MSBuild "48 Warning(s)"
46 diagnostics uniques (44 + 2)   ← correspond au compte "46" du build incrémental précédent
```

L'hypothèse la plus probable pour le "46" du build incrémental précédent : le moteur
incrémental de MSBuild a réutilisé un état de compilation partiel (le conteneur avait déjà vu
un `dotnet restore`/build partiel dans la même session), ce qui a supprimé une des deux
émissions des 2 diagnostics dupliqués par Roslyn — sans jamais faire disparaître ni apparaître
un diagnostic réel.

### 2.4 Verdict du gate

```
WARNINGS_REPORTED_LEGAL_READINESS: 48 (commit e1c4770, rapport original)
WARNINGS_REPORTED_PREFLIGHT: 46 (commit e1c4770/6fab329, build incrémental)
WARNINGS_REPRODUCED_BASELINE (e1c4770, méthode clean+no-incremental+sln): 48 (46 fingerprints uniques)
WARNINGS_REPRODUCED_CURRENT (6fab329, méthode identique): 48 (46 fingerprints uniques)
WARNING_FINGERPRINT_DELTA: 0 — ensembles de fingerprints strictement IDENTIQUES entre e1c4770 et 6fab329
EXPLANATION: différence de méthode d'invocation de build (incrémental vs clean --no-incremental
  sur la solution complète), pas une différence de code. Deux diagnostics CS8602 sont émis deux
  fois par Roslyn au même emplacement, ce qui fait varier le compte "N Warning(s)" de MSBuild
  (48) par rapport au compte de diagnostics uniques (46) selon que le moteur incrémental
  réutilise ou non l'état de compilation précédent.
NEW_CODE_WARNING_FINGERPRINTS: 0 — gate satisfait, preuve par diff direct d'un worktree isolé.
```

**Recommandation opérationnelle pour les lots futurs** : toujours utiliser
`dotnet clean Cosmechic.sln -c Release && dotnet build Cosmechic.sln -c Release
--no-incremental` comme méthode de référence pour tout comptage d'avertissements cité dans un
rapport PM, afin d'éliminer cette source de variance non liée au code.

---

## Section 3 — Recertification technique

Exécutée avec la méthode de référence ci-dessus (`Cosmechic.sln`, `-c Release`) :

| Gate | Résultat |
|---|---|
| `dotnet restore Cosmechic.sln` | PASS |
| `dotnet build Cosmechic.sln -c Release --no-incremental` | PASS — 0 erreur, 48 avertissements MSBuild (46 fingerprints uniques, voir section 2) |
| `dotnet test Cosmechic.sln -c Release --no-build` | **399/399 PASS**, 0 échec, 0 ignoré |
| `dotnet list package --vulnerable --include-transitive` | **0 vulnérabilité** (Cosmechic, Cosmechic.Utility, Cosmechic.Tests) |
| `dotnet ef migrations has-pending-model-changes` (`CosmechicsContext`) | **NONE** — "No changes have been made to the model since the last migration." |
| `dotnet ef migrations has-pending-model-changes` (`ApplicationDbContext`) | **NONE** |
| Migrations créées ce lot | 0 |

Aucun changement de code n'a été effectué pour obtenir ces résultats.

---

## Section 4 — Analyse du gap d'anonymisation AspNetUsers

### 4.1 Constat corrigé par rapport au preflight précédent

Le preflight précédent qualifiait ces champs de traces possiblement "fantômes"/"legacy" en se
fondant sur un commentaire de code (`CustomerAddress.cs`) affirmant qu'ils "ne sont utilisés
nulle part dans le checkout actuel". **Ce commentaire est exact pour le checkout, mais
incomplet** : une inspection ligne par ligne révèle que ces colonnes sont **activement lues et
écrites par un écran d'administration complet** (`AspNetUsersController`, actions
`Create`/`Edit`/`Details`/`Index`/`Delete`, toutes `[Authorize(Roles = "Admin")]`). La
classification correcte est donc **`ACTIVE`**, pas `LEGACY_UNUSED`.

### 4.2 Double représentation du même schéma physique (architecture ARCH-002)

Deux `DbContext` distincts mappent les **quatre mêmes colonnes physiques** de la table SQL
`AspNetUsers` (ajoutées par une seule migration `ApplicationDbContext`-owned) :

1. `Cosmechic.Models.AspNetUser` (`CosmechicsContext`, scaffold database-first) : **propriétés
   CLR réelles** `StreetAddress/City/State/PostalCode` — consommées par
   `AspNetUsersController`.
2. `ApplicationDbContext.OnModelCreating` (`Cosmechic/Data/ApplicationDbContext.cs:36-42`) :
   **propriétés fantômes EF** (shadow properties) sur `IdentityUser`, sans propriété CLR — les
   seuls consommateurs identifiés sont `IdentitySqlServerTests.cs` (code de test uniquement,
   via `context.Entry(user).Property("StreetAddress")`).

### 4.3 `ASPNETUSERS_LEGACY_ADDRESS_MATRIX`

| FIELD | DB_COLUMN | CURRENT_WRITER | CURRENT_READER | PERSONAL_DATA | ANONYMIZED_TODAY | EXPORTED_TODAY | SAFE_REMEDIATION_OPTIONS | SCHEMA_CHANGE_REQUIRED |
|---|---|---|---|---|---|---|---|---|
| Adresse (rue) | `AspNetUsers.StreetAddress` (nvarchar(max), nullable) | `AspNetUsersController.Create`/`Edit` (POST, admin), via `Cosmechic.Models.AspNetUser` (`CosmechicsContext`) | `AspNetUsersController.Details`/`Index`/`Delete`/`Edit` (GET, admin), vues `Views/AspNetUsers/*.cshtml` | OUI — adresse postale d'un client | **NON** — `AccountAnonymizationService.AnonymizeAsync` ne touche jamais ces colonnes, via aucun des deux DbContext | **NON** — `DownloadPersonalData.OnPostAsync` n'énumère que `typeof(IdentityUser).GetProperties()` filtré par `[PersonalDataAttribute]` ; une propriété fantôme EF n'a pas d'attribut CLR et ne peut structurellement pas apparaître dans cette réflexion. Le modèle `CosmechicsContext.AspNetUser` (qui, lui, a une vraie propriété CLR) n'est jamais interrogé par ce code d'export non plus. | (a) Étendre `AccountAnonymizationService.AnonymizeAsync` pour écraser ces 4 champs (même patron que `OrderHeader.StreetAddress` déjà anonymisé) ; (b) Étendre l'export personnel pour les inclure ; (c) Évaluer la dépréciation/suppression de cet écran admin s'il est jugé intégralement remplacé par `CustomerAddress` | NON pour (a)/(b) — colonnes déjà existantes, seul du code de service change. OUI seulement si (c) suppression de colonnes est retenue. |
| Ville | `AspNetUsers.City` | idem | idem | OUI | NON | NON | idem | idem |
| Province/État | `AspNetUsers.State` | idem | idem | OUI | NON | NON | idem | idem |
| Code postal | `AspNetUsers.PostalCode` | idem | idem | OUI | NON | NON | idem | idem |

### 4.4 Classification finale

**`ACTIVE`** (via `AspNetUsersController`, surface admin uniquement, jamais utilisée dans le
parcours client depuis que `CartController.Summary`/`SummaryPOST` ont été réécrits en
COSMECHIC-ACCOUNT-001 pour s'appuyer exclusivement sur `CustomerAddress`).

**Confirmation du risque signalé au preflight précédent** : si un administrateur a un jour
renseigné une adresse via `/AspNetUsers/Edit/{id}`, cette donnée personnelle **survit
intégralement** à une anonymisation de compte demandée par le client (`AccountAnonymizationService`
ne la touche pas) et **n'est jamais visible au client lui-même** dans son propre export de
données personnelles (asymétrie admin-visible / client-invisible / jamais anonymisée).

---

## Section 5 — Analyse du gap ShoppingCart

### 5.1 Modèle de persistance

`ShoppingCart` (`Cosmechic/Models/ShoppingCart.cs`) : `Id, ProduitId, Count,
ApplicationUserId (string?, nullable en C#), Produit (navigation)`. Une seule table, pas de
distinction "panier anonyme" vs "panier authentifié" dans le schéma — la distinction se fait
uniquement par la présence ou non d'un `ApplicationUserId`.

### 5.2 Constat par le code : le panier anonyme n'existe pas

Le seul point d'écriture de `ShoppingCart` est `ProduitsController.ItemDetails` (POST,
`Cosmechic/Controllers/ProduitsController.cs:686-693`), marqué **`[Authorize]`** au niveau de
l'action, et qui dérive `ApplicationUserId` exclusivement de
`ClaimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value` (jamais du corps de la requête).
**Conclusion factuelle : bien que le champ C# soit nullable, aucun panier anonyme ne peut
exister en pratique — toute ligne `ShoppingCart` appartient nécessairement à un utilisateur
authentifié au moment de sa création.**

### 5.3 Cycle de vie observé

- **Ajout** : `ProduitsController.ItemDetails` (POST) — crée ou incrémente la ligne pour
  `(ApplicationUserId, ProduitId)`.
- **Modification/suppression manuelle** : `CartController.cs:287,315,329,355` (Plus/Minus/
  Remove, `[Authorize]` + vérification d'ownership déjà auditée en COSMECHIC-SECURITY-002).
- **Vidage automatique** : **uniquement après un paiement Stripe confirmé et un fulfillment
  réussi** — `StripeFulfillmentService.cs:220-225` supprime **toutes** les lignes de panier de
  l'utilisateur de la commande (`context.ShoppingCarts.RemoveRange(cartItems)`), à l'intérieur
  de la même transaction que la confirmation de commande. Un panier n'est donc **jamais** vidé
  simplement par l'affichage de la page de confirmation (commentaire explicite dans le code).
- **Panier abandonné (jamais payé)** : **aucun mécanisme de purge n'existe** — cohérent avec le
  constat déjà établi (aucune tâche planifiée nulle part dans ce dépôt). Une ligne de panier
  ajoutée puis jamais finalisée reste en base indéfiniment.
- **Après anonymisation du compte** : `AccountAnonymizationService.AnonymizeAsync` **ne
  référence jamais `ShoppingCarts`** — confirmé par lecture complète du service (section 4 du
  précédent audit, revérifiée ici). Une ligne de panier abandonnée reste donc liée à
  `ApplicationUserId` d'un compte anonymisé, sans jamais être supprimée ni détachée.

### 5.4 `SHOPPING_CART_PRIVACY_MATRIX`

| DATA | OWNER_REFERENCE | PERSONAL_DATA | RETENTION_CURRENT | ACCOUNT_ANONYMIZATION_EFFECT | ORPHAN_RISK | REMEDIATION_OPTIONS |
|---|---|---|---|---|---|---|
| Panier authentifié (`anonymous cart`) | — | — | **N'EXISTE PAS** — prouvé par le code : `[Authorize]` sur le seul point d'écriture, aucun `ApplicationUserId` null observable en pratique | N/A | N/A | N/A |
| Panier authentifié actif (`authenticated cart`) | `ApplicationUserId` (FK `AspNetUsers`, non nul en pratique) | Indirect — révèle un intérêt d'achat identifié pour un produit donné, ce qui constitue une donnée personnelle au sens large (Loi 25/PIPEDA : toute information se rapportant à une personne identifiable) | Vécu tant que le compte est actif et que le produit n'est ni acheté ni retiré manuellement | Non affecté tant que le compte n'est pas anonymisé | Aucun tant que le compte reste actif | Aucune action requise pour ce cas |
| Panier finalisé (`historical cart`) | — | — | **N'EXISTE PAS comme concept séparé** — pas de table d'archive de panier ; une commande finalisée transfère ses données vers `OrderHeader`/`OrderDetail` (déjà auditées) et la ligne `ShoppingCart` correspondante est purement et simplement supprimée | N/A (déjà supprimé au moment du paiement) | N/A | Aucune |
| Panier abandonné/orphelin (`orphaned cart`) | `ApplicationUserId` pointant potentiellement vers un compte anonymisé | OUI si le compte lié n'a jamais été anonymisé (identifie un client réel) ; résiduel même après anonymisation (le lien `ApplicationUserId` reste techniquement valide vers un compte désormais anonymisé, mais la ligne elle-même n'est ni supprimée ni neutralisée) | **Illimitée — aucune purge, jamais** | **NON COUVERT** — confirmé par lecture complète de `AccountAnonymizationService.cs` | **RISQUE CONFIRMÉ** : la ligne survit indéfiniment à l'anonymisation du compte propriétaire | (a) Étendre `AccountAnonymizationService.AnonymizeAsync` pour supprimer les lignes `ShoppingCarts` de l'utilisateur (même traitement que `CustomerAddress`, cohérent car le panier est lui aussi une donnée non-transactionnelle/vivante) ; (b) Définir une politique métier de péremption du panier abandonné (nécessiterait un mécanisme de purge par ancienneté — **explicitement hors périmètre de ce lot**, qui interdit la création de tout scheduler/cron) ; (c) Accepter le risque tel quel et le documenter comme limitation connue |

### 5.5 Verdict

Le gap est confirmé et précisément qualifié : **la seule catégorie de panier réellement à
risque est le panier abandonné/orphelin**, parce qu'`AccountAnonymizationService` ne le
couvre pas. Le panier anonyme n'existe pas (protection déjà assurée par construction) et le
panier "historique" n'existe pas comme concept distinct (déjà supprimé à la finalisation).

---

## Section 6 — Recherche juridique officielle

### 6.1 Limitation d'environnement à déclarer explicitement

**Le proxy d'accès réseau de cet environnement bloque l'accès direct (`WebFetch`) à tous les
domaines gouvernementaux testés** : `opc.gouv.qc.ca`, `revenuquebec.ca`, `canada.ca`,
`legisquebec.gouv.qc.ca`, `priv.gc.ca`. Il n'a donc pas été possible de citer un extrait de
texte directement récupéré et vérifié depuis la page source elle-même dans cette session.

Les constats ci-dessous proviennent de recherches web (`WebSearch`) dont le moteur a lui-même
consulté et synthétisé ces pages officielles (les URLs exactes des pages sources sont citées
pour chaque affirmation). **Ce ne sont pas des citations textuelles vérifiées ligne par
ligne par Claude** — elles doivent être confirmées par une consultation directe des textes
officiels (ou par un avis juridique) avant toute décision finale. Ceci est déclaré
explicitement plutôt que présenté comme une lecture directe et certaine du texte de loi.

### 6.2 Tableau des affirmations sourcées

| # | CLAIM | OFFICIAL_SOURCE | SOURCE_DATE_OR_CURRENT_STATUS | WHAT_THE_SOURCE_ACTUALLY_ESTABLISHES | LIMITATIONS | IMPACT_FOR_COSMECHIC |
|---|---|---|---|---|---|---|
| 1 | La garantie légale de conformité/durabilité (LPC) oblige le commerçant à réparer, échanger ou rembourser un bien défectueux/non conforme, **indépendamment de toute politique de retour/échange qu'il ait adoptée ou non**. | [opc.gouv.qc.ca — Garanties prévues par la loi](https://www.opc.gouv.qc.ca/commercant/pratique-commerce/garanties/legale), [opc.gouv.qc.ca — Échange et remboursement d'un bien défectueux](https://www.opc.gouv.qc.ca/en/consumer/topic/exchanging-layaways/refunding/goods/defective-goods) | Contenu courant du site OPC (non daté précisément dans les extraits obtenus) | Un droit impératif du consommateur, non renonçable par contrat/politique commerciale, tiré de la *Loi sur la protection du consommateur* (arts. 37-53 généralement cités pour la garantie légale, numéros exacts non vérifiés directement dans cette session — voir limitation) | Numéros d'article non confirmés par lecture directe du texte légal (`legisquebec.gouv.qc.ca` bloqué dans cet environnement) | Toute politique Cosmechic d'exclusion des retours de produits cosmétiques ouverts **ne peut en aucun cas s'appliquer à un produit réellement défectueux ou non conforme** — ce cas doit rester traité indépendamment de la politique commerciale volontaire. |
| 2 | Pour un contrat à distance (vente en ligne), le commerçant doit divulguer avant la conclusion du contrat un ensemble d'informations prévues à l'art. 54.4 LPC (nom, adresse, téléphone, description du bien, prix détaillé, mode/délai de livraison, **conditions d'annulation/résiliation/retour/échange/remboursement**). | [lpc.quebec — Article 54.4](https://lpc.quebec/articles/article-54-4/), [publicitecomportementale.openum.ca — LPC 54.1](https://publicitecomportementale.openum.ca/articles/article-54-1/) | Contenu courant | Obligation positive de divulgation préalable de la politique de retour, quelle qu'elle soit — Cosmechic doit donc énoncer clairement sa politique cosmétique-ouverte, pas seulement l'appliquer. | `lpc.quebec` est un site tiers de vulgarisation, pas le texte officiel lui-même (`legisquebec.gouv.qc.ca` inaccessible dans cet environnement) — numéros d'article à reconfirmer sur le texte officiel. | La page `Home/Returns.cshtml` actuelle doit, au minimum, énoncer explicitement la politique retenue une fois décidée — elle ne peut pas rester silencieuse sur le cas des produits ouverts si Cosmechic adopte une exclusion. |
| 3 | Si le commerçant ne divulgue pas correctement les informations de l'art. 54.4, le consommateur bénéficie d'un **droit de résolution de 7 jours** après réception du contrat ; en cas de résolution, il doit restituer le bien dans les 15 jours et le commerçant assume les frais raisonnables de restitution. | [lpc.quebec — Article 54.4](https://lpc.quebec/articles/article-54-4/) | Contenu courant | Un droit de rétractation **conditionnel à un défaut de divulgation du commerçant**, pas un droit général de remords pour tout achat en ligne. | Idem limitation #2. | Si Cosmechic ne documente pas clairement sa politique de retour lors du parcours d'achat, elle s'expose à ce droit de résolution de 7 jours même pour un produit non défectueux. |
| 4 | Les biens descellés par le consommateur après livraison, non renvoyables pour des raisons d'hygiène/protection de la santé, seraient exclus d'un droit de rétractation. | Résultat de recherche synthétisé, source précise ambiguë — un des résultats de la même recherche mentionnait la "Loi Hamon" (droit **français**, pas québécois) dans le même lot de résultats | **NON CONFIRMÉ COMME APPLICABLE AU QUÉBEC** | **Ce point est explicitement signalé comme INCERTAIN** : le moteur de recherche a pu conflater une règle de droit français (Code de la consommation, exclusion hygiène/santé du droit de rétractation, art. L221-28) avec le contexte québécois interrogé. Aucune confirmation indépendante qu'une exclusion équivalente, codifiée de la même façon, existe dans la LPC québécoise n'a été obtenue. | **`LEGAL_COUNSEL_CONFIRMATION_REQUIRED=YES`** — ne jamais fonder une politique d'exclusion "produit cosmétique ouvert" sur cette affirmation sans vérification directe du texte légal québécois (et non d'une source française) par un juriste. |
| 5 | Sous la *Loi canadienne sur la sécurité des produits de consommation* (LCSPC/CCPSA), un vendeur (pas seulement fabricant/importateur) qui prend connaissance d'un incident impliquant un produit de consommation a **2 jours** pour transmettre un rapport à Santé Canada contenant l'information dont il dispose ; fabricants/importateurs doivent fournir un rapport complémentaire sous 10 jours. | [bennettjones.com — CCPSA obligations](https://www.bennettjones.com/Insights/Updates/The-Canada-Consumer-Product-Safety-Act-New-Obligations-for-Manufacturers-Importers-and-Sellers-of-Consumer-Products), [canada.ca — Guide on Mandatory Reporting under CCPSA, s.14](https://www.canada.ca/en/health-canada/services/consumer-product-safety/legislation-guidelines/acts-regulations/canada-consumer-product-safety-act/industry/guide-mandatory-reporting-section-14-profile.html) | Guide industrie Santé Canada, contenu courant | Obligation légale fédérale positive de signalement rapide en cas d'incident de sécurité produit, applicable aux **vendeurs** (donc potentiellement Cosmechic elle-même, en tant que détaillant en ligne) | Le rôle exact de Cosmechic (revendeur vs importateur si elle importe elle-même les produits) déterminerait l'étendue précise de l'obligation — à confirmer. | **Constat majeur** : le scénario "réaction/allégation de sécurité" du preflight précédent n'a **aucun mécanisme technique de détection ni d'escalade sous 2 jours** dans le code actuel (texte libre non catégorisé, aucune alerte). C'est un écart de conformité potentiel distinct de la seule politique de retour. |
| 6 | Seuil de "petit fournisseur" pour l'inscription obligatoire à la TPS (fédéral) : 30 000 $ de fournitures taxables sur un trimestre civil donné OU sur l'ensemble des 4 trimestres civils précédents. | [canada.ca — When to register for and start charging the GST/HST](https://www.canada.ca/en/revenue-agency/services/tax/businesses/topics/gst-hst-businesses/when-register-charge.html) | Contenu courant CRA | Règle fédérale claire et bien établie, calcul sur fenêtre glissante de 4 trimestres ou dépassement en un seul trimestre. | Aucune (règle stable et bien documentée). | Détermine si Cosmechic a même besoin d'un numéro TPS — dépend du chiffre d'affaires réel, inconnu de ce dépôt. |
| 7 | Seuil de "petit fournisseur" pour l'inscription à la TVQ (Québec) : identique, 30 000 $, même mécanique de calcul. | [revenuquebec.ca — Particularités concernant le petit fournisseur](https://www.revenuquebec.ca/fr/citoyens/taxes/biens-et-services-taxables-detaxes-ou-exoneres/tps-et-tvq/autres-situations/particularites-concernant-le-petit-fournisseur/) | Contenu courant Revenu Québec | Règle provinciale miroir de la règle fédérale. | Aucune. | Idem #6, pour la TVQ. |
| 8 | Format du numéro de TVQ : `1234567890 TQ 0001`. Une facture doit afficher séparément les montants de TPS et de TVQ, avec les numéros d'inscription correspondants, une fois le commerçant inscrit. Le numéro de TPS/TVH doit apparaître sur toute facture ≥ 30 $ CAD ; les informations complètes (dont le nom de l'acheteur) sont requises ≥ 150 $ CAD pour les crédits de taxe sur intrants de l'acheteur. | Synthèse de recherche (sources multiples non gouvernementales : `mafacture.ca`, `justinvoice.ca`, `bookkeeping-essentials.ca`) recoupant les règles CRA/Revenu Québec | Contenu courant, sources tierces | Ces seuils (30 $/150 $) et exigences de champs sont un résumé cohérent et répété par plusieurs sources indépendantes de la règle fédérale ITC (crédits de taxe sur intrants), documentée officiellement par le CRA (`canada.ca/.../8-4/documentary-requirements...`) | Ces sources ne sont pas elles-mêmes gouvernementales (agrégateurs/blogues comptables) — la règle des seuils 30 $/150 $ concerne spécifiquement les **crédits de taxe sur intrants de l'acheteur**, pas nécessairement une obligation légale identique pour Cosmechic vendant à des consommateurs finaux (qui ne réclament généralement pas de CTI) — distinction à faire confirmer. | Si Cosmechic s'inscrit à la TPS/TVQ, ces seuils de champs obligatoires par palier de montant sont la meilleure référence disponible pour concevoir l'affichage futur du reçu/facture, sous réserve de confirmation. |
| 9 | Les registres et pièces justificatives (comptables/fiscaux) doivent généralement être conservés **6 ans** après la dernière année à laquelle ils se rapportent (Revenu Québec) ; règle fédérale équivalente sous l'art. 230 de la *Loi de l'impôt sur le revenu* — également 6 ans. Revenu Québec peut exiger une conservation plus longue en cas d'opposition/appel, ou autoriser une destruction anticipée sur demande écrite signée. | [revenuquebec.ca — Conserver vos registres et vos pièces justificatives](https://www.revenuquebec.ca/fr/entreprises/retenues-a-la-source-et-cotisations-de-lemployeur/registres-et-pieces-justificatives/) | Contenu courant Revenu Québec | Règle de rétention comptable/fiscale bien établie et cohérente entre les deux paliers de gouvernement. | Une des recherches a mentionné, dans une phrase isolée non directement sourcée à une page officielle, "7 ans" pour les informations financières — chiffre en contradiction avec le "6 ans" confirmé par la page Revenu Québec elle-même. **Écart non résolu, à confirmer avec un comptable/fiscaliste.** | Détermine la durée plancher **minimale** en dessous de laquelle `OrderHeader`/`Refund`/données fiscales liées **ne peuvent légalement pas être supprimées**, quelle que soit la préférence de minimisation de données. |
| 10 | Sous la *Loi 25*, il n'existe **aucune durée de conservation fixe imposée par la loi elle-même** pour les renseignements personnels détenus par une entreprise privée : le principe est de conserver "aussi longtemps que raisonnablement nécessaire" pour les finalités de la collecte, puis de détruire ou anonymiser de façon sécuritaire — sous réserve d'autres lois (ex. lois fiscales) pouvant imposer une durée minimale plus longue. | [priv.gc.ca — Conservation et retrait des renseignements personnels](https://www.priv.gc.ca/fr/sujets-lies-a-la-protection-de-la-vie-privee/protection-des-renseignements-personnels-pour-les-entreprises/atteintes-et-mesures-de-securite/securite-des-renseignements-personnels/gd_rd_201406/) | Contenu courant CPVP (fédéral, principes généraux LPRPDE cohérents avec la Loi 25 québécoise) | Principe de minimisation fondé sur la finalité, pas une durée numérique fixe. | La page directement citée est fédérale (LPRPDE) ; la Loi 25 (québécoise, régime applicable à Cosmechic comme entreprise privée au Québec) partage ce même principe de finalité selon les sources consultées, mais le texte spécifique de la Loi 25 elle-même n'a pas pu être directement consulté (`legisquebec.gouv.qc.ca` bloqué). | Confirme qu'aucune durée arbitraire ne doit être choisie sans lien avec une finalité réelle (opérationnelle) ou une obligation externe (fiscale) identifiée — exactement la posture déjà adoptée dans ce lot (aucune durée n'est fixée). |
| 11 | Sous la Loi 25, le registre des incidents de confidentialité doit être conservé un minimum de **5 ans** après la date où l'organisation a pris connaissance de l'incident — une obligation plus stricte que l'ancienne règle fédérale (24 mois). | [portail-assurance.ca — Registre québécois des incidents](https://portail-assurance.ca/article/donnees-personnelles-le-registre-quebecois-des-incidents-devra-etre-conserve-cinq-ans/), texte réglementaire cité : *Règlement sur les incidents de confidentialité* (A-2.1, r. 3.1) | Contenu courant | Obligation de rétention spécifique, distincte de la rétention des données clients elles-mêmes — concerne un **registre d'incidents de sécurité**, pas les commandes/comptes. | Le règlement lui-même (`legisquebec.gouv.qc.ca`) n'a pas pu être directement consulté. | **Hors périmètre direct des trois décisions PM**, mais pertinent si Cosmechic subit un jour un incident de confidentialité : un tel registre (actuellement inexistant dans ce dépôt — aucune table `SecurityIncident` ou équivalent) devrait être conservé 5 ans une fois créé. |

### 6.3 Sources consultées (hors gouvernemental direct, en raison du blocage réseau)

Toutes les recherches ont utilisé l'outil `WebSearch`, qui interroge et synthétise
publiquement le contenu de pages gouvernementales officielles même lorsque `WebFetch` ne peut
pas les récupérer directement dans cet environnement. `OFFICIAL_SOURCES_COUNT` (pages
`.gouv.qc.ca`/`.canada.ca`/`.gc.ca` citées comme source d'au moins une affirmation ci-dessus,
même via synthèse `WebSearch`) = **7** distinctes (`opc.gouv.qc.ca` ×2 pages, `revenuquebec.ca`
×2 pages, `canada.ca` ×2 pages, `priv.gc.ca` ×1 page) — voir tableau 6.2 pour le détail exact
par affirmation.

---

## Section 7 — Sujet A : produits cosmétiques ouverts

| SCENARIO | LEGAL_BASELINE | MERCHANT_DISCRETION | CAN_REJECT_BY_POLICY | MANDATORY_EXCEPTION | RECOMMENDED_COSMECHIC_POLICY | LEGAL_COUNSEL_REQUIRED |
|---|---|---|---|---|---|---|
| Produit non ouvert, changement d'avis | Aucun droit général de remords sous la LPC pour un contrat où le commerçant a rempli ses obligations (affirmation #1/#2) | Totale, sous réserve de la divulguer clairement avant l'achat (art. 54.4, affirmation #2) | OUI | Aucune | Aucune recommandation formulée ici (décision commerciale du PM) — techniquement, toute politique choisie doit être énoncée explicitement sur `Home/Returns.cshtml` et au parcours d'achat | OUI — pour valider la formulation de divulgation exigée par l'art. 54.4 |
| Produit ouvert, changement d'avis (pas de défaut allégué) | Idem — aucun droit impératif identifié à ce stade | Totale, sous la même réserve de divulgation | OUI | Aucune confirmée (voir réserve affirmation #4, incertaine) | Idem — décision PM à formuler et divulguer clairement | **OUI, critique** — en particulier pour vérifier l'affirmation #4 (exclusion hygiène) avant de s'y appuyer |
| Produit utilisé, changement d'avis | Idem "produit ouvert" | Totale, sous réserve de divulgation | OUI | Aucune confirmée | Idem | OUI |
| Produit endommagé à la réception (transport) | Vraisemblablement couvert par la garantie légale de conformité (le bien livré n'est pas conforme à ce qui a été vendu) — affirmation #1 | Limitée — le remboursement/échange/réparation est dû si le produit n'est pas conforme, indépendamment de la politique | **NON** si qualifié de non-conformité | OUI — garantie légale, non renonçable | Traiter distinctement de "changement d'avis" : accepter sans les mêmes limites de délai que la politique volontaire | OUI — pour confirmer la qualification exacte (transport vs défaut du bien lui-même) |
| Défaut du produit (non-conformité) | Couvert directement par la garantie légale de conformité/durabilité (affirmation #1) | Aucune — obligation légale de réparer/échanger/rembourser | **NON** | OUI — impératif | Traiter comme un droit acquis du client, jamais soumis à la politique commerciale volontaire de retour | OUI — même remarque que ci-dessus sur les numéros d'article exacts |
| Mauvais article expédié (erreur Cosmechic) | Non-conformité du bien livré par rapport au bien commandé — probablement couvert par la garantie légale | Aucune | **NON** | OUI, vraisemblable | Remboursement intégral incluant frais de port (déjà la logique de `RefundCause.MerchantFault`, cohérente) | OUI, pour confirmer la qualification légale exacte |
| Erreur du marchand (cas général) | Dépend de la nature exacte de l'erreur | Variable selon le cas | Dépend du cas | Possible selon le cas | Décision PM au cas par cas, avec le mécanisme `RefundCause.MerchantFault` déjà disponible | Selon le cas |
| Réaction/allégation de sécurité | **Obligation fédérale positive de signalement sous 2 jours à Santé Canada si l'incident est confirmé** (affirmation #5, LCSPC art. 14) — distincte de la question du retour lui-même | Aucune sur l'obligation de signalement ; discrétion sur la politique de retour elle-même | Le retour peut suivre la politique choisie, **mais le signalement réglementaire est indépendant et non discrétionnaire** | OUI — obligation de signalement, pas d'exclusion contractuelle possible | **Recommandation opérationnelle indépendante de toute politique de retour** : mettre en place un canal de signalement distinct du formulaire de retour standard, avec triage rapide (délai de 2 jours à respecter) | **OUI, critique** — déterminer si Cosmechic est "vendeur" au sens de la LCSPC et l'étendue exacte de son obligation |
| Produit rendu sale/souillé (constaté à la réception du retour) | Pas d'obligation légale identifiée spécifique à ce cas | Totale — décision commerciale de refuser un remboursement post-approbation si l'état constaté est jugé inacceptable | OUI, sous réserve de ne pas l'appliquer à un cas de non-conformité initiale déjà couvert plus haut | Aucune identifiée | Décision PM ; nécessiterait un champ d'observation admin au moment de `MarkReceivedAsync` (actuellement inexistant) | Optionnel |
| Remboursement (mécanique) | N/A — question de mise en œuvre, pas de droit distinct | N/A | N/A | N/A | Déjà fonctionnel (`RefundOrchestrationService`) | Non |
| Échange | N/A — fonctionnalité absente du logiciel | Totale — aucune obligation légale identifiée d'offrir un échange plutôt qu'un remboursement | N/A | Aucune identifiée | Décision PM sur l'opportunité de développer cette fonctionnalité à l'avenir | Non, sauf si liée à un cas de non-conformité (où remboursement/réparation/échange sont des alternatives légalement équivalentes selon l'affirmation #1) |

**Point de vigilance transversal** : la distinction structurelle la plus importante que ce
tableau révèle n'est **pas** "ouvert vs non ouvert", mais **"remords/préférence" vs
"non-conformité/défaut/erreur"** — cette dernière catégorie bénéficie très probablement d'une
protection légale impérative (garantie légale de conformité) qui ne peut pas être écartée par
une politique interne, peu importe l'état d'ouverture du produit.

---

## Section 8 — Sujet B : rétention des données (matrice enrichie)

Reprend les 15 catégories du preflight précédent, enrichies des colonnes demandées. Aucune
durée n'est choisie ici — seules les contraintes externes identifiées (section 6) sont notées
comme plancher potentiel.

| DATA_CATEGORY | BUSINESS_PURPOSE | LEGAL_OR_ACCOUNTING_REQUIREMENT | PRIVACY_MINIMIZATION_REQUIREMENT | SUGGESTED_RETENTION_MODEL | START_EVENT | DELETION_OR_ANONYMIZATION | LEGAL_CONFIRMATION_REQUIRED |
|---|---|---|---|---|---|---|---|
| Compte client (`AspNetUsers`) | Authentification, identité de compte | Aucune obligation de conservation identifiée spécifiquement pour le compte lui-même | Loi 25 : conserver seulement tant que nécessaire à la finalité (affirmation #10) | Anonymisation sur demande (déjà en place) ; pas de suppression automatique par ancienneté proposée ici | Demande explicite du client (`DeletePersonalData`) | Anonymisation (déjà en place, avec les 2 gaps identifiés sections 4/5 à combler) | OUI — confirmer si l'anonymisation actuelle suffit au sens de la Loi 25 |
| Adresses (`CustomerAddress`) | Carnet d'adresses vivant | Aucune identifiée | Idem | Suppression déjà en place à l'anonymisation | Demande du client | Suppression (déjà en place) | Faible priorité |
| Commande (`OrderHeader`) | Historique transactionnel, preuve de vente | **Plancher de 6 ans après la dernière année fiscale concernée** (affirmation #9, Revenu Québec + fédéral, sous réserve de l'écart 6 vs 7 ans à confirmer) | Doit être mise en balance avec l'obligation comptable — la minimisation ne peut pas aller plus vite que le plancher fiscal | Conservation intégrale au moins 6 ans (voire 7 selon confirmation), anonymisation partielle des champs directement identifiants déjà en place au-delà de la demande client | Date de la commande / fin de l'année fiscale concernée | Anonymisation partielle déjà en place (jamais de suppression avant le plancher fiscal) | **OUI, prioritaire** — trancher l'écart 6 vs 7 ans avec un comptable |
| Détails de commande (`OrderDetail`) | Lignes d'achat | Suit `OrderHeader` (même ensemble transactionnel) | Idem | Idem `OrderHeader` | Idem | Idem | Suit `OrderHeader` |
| Demande de retour (`ReturnRequest`) | Trace du processus de retour | Probablement rattachée à la même obligation comptable si elle affecte un remboursement (donc potentiellement le même plancher que `Refund`) | Le texte libre (`Reason`/`CustomerComment`/`AdminComment`) peut contenir des données sensibles (allégation de réaction — voir section 7) justifiant une minimisation renforcée indépendante du plancher comptable | Non déterminé — dépend de la décision B du PM | Création de la demande | Non couvert par l'anonymisation actuelle (déjà signalé au preflight précédent) | OUI |
| Lignes de retour (`ReturnItem`) | Détail des articles retournés | Suit `ReturnRequest` | Idem | Idem | Idem | Idem | Suit `ReturnRequest` |
| Remboursement (`Refund`) | Preuve comptable de remboursement | **Même plancher que `OrderHeader`** (pièce justificative fiscale) | — | Conservation intégrale au moins le plancher fiscal | Date du remboursement | Aucune suppression avant le plancher fiscal | OUI — même écart 6/7 ans à trancher |
| Historique de statut (`OrderStatusHistory`) | Piste d'audit des transitions de commande | Non identifiée spécifiquement, probablement alignée sur `OrderHeader` par cohérence d'audit | — | Aligné sur `OrderHeader` par défaut proposé (à valider) | Chaque transition | Non couvert par l'anonymisation (FK vers acteur potentiellement anonymisé) | Faible priorité |
| Mouvements de stock (`StockMovement`) | Ledger d'audit d'inventaire | Probablement une obligation comptable générale de tenue de registres (affirmation #9 pourrait s'étendre à l'inventaire) | — | Non déterminé | Chaque mouvement | Non couvert par l'anonymisation (acteur = généralement personnel, pas client) | Priorité faible à moyenne |
| Événements Stripe traités (`ProcessedStripeEvent`) | Idempotence technique webhook | Aucune — pas de donnée personnelle (confirmé, aucun champ nominatif) | N/A | Non déterminé — croissance non bornée signalée comme risque opérationnel (pas de protection des données) | Réception de l'événement | Aucune purge | NON — donnée technique |
| Messages de contact | N/A | N/A | N/A | N/A — **rien n'est persisté par ce code** (email uniquement) | N/A | N/A | Hors périmètre applicatif — dépend du fournisseur SMTP externe |
| Journaux techniques | N/A | N/A | N/A | N/A — **aucun sink persistant dans ce dépôt** | N/A | N/A | Hors périmètre applicatif — dépend de l'hébergement futur |
| Registre d'incidents de confidentialité | Conformité Loi 25 en cas d'incident de sécurité réel | **5 ans minimum après prise de connaissance de l'incident** (affirmation #11) si un tel registre existe un jour | — | **N'existe pas encore dans ce dépôt** — aucune table dédiée | Survenue d'un incident | N/A tant qu'aucun incident n'a eu lieu | OUI, si/quand un tel registre est créé |
| Avis clients (`TemoignagesClient`) | Avis produit public | Aucune identifiée | Le champ `Nom` en texte libre, sans FK vers `AspNetUsers`, limite structurellement toute automatisation d'une demande de suppression individuelle (déjà signalé) | Non déterminé | Publication de l'avis | Suppression manuelle admin uniquement (hors flux de protection des données) | Priorité faible |
| Panier (`ShoppingCart`) | Panier d'achat en cours | Aucune | Devrait suivre le même principe que `CustomerAddress` (donnée vivante, non transactionnelle) selon la recommandation de la section 5.4 | Suppression à l'anonymisation recommandée (gap section 5) | Ajout au panier | **Non couvert aujourd'hui** (gap confirmé section 5) | Faible priorité légale, mais **gate de confidentialité PM** |
| Champs fantômes AspNetUsers (adresse) | Écran admin CRUD (voir section 4) | Aucune identifiée spécifiquement | Devrait suivre le même principe que le compte client lui-même une fois anonymisé | Anonymisation recommandée en même temps que le compte | Saisie admin | **Non couvert aujourd'hui** (gap confirmé section 4) | Faible priorité légale, mais **gate de confidentialité PM** |

---

## Section 9 — Sujet C : facture / reçu / taxes

### 9.1 Distinction terminologique (exigée par la directive)

| Notion | Ce que c'est | Statut actuel dans Cosmechic |
|---|---|---|
| `ORDER_RECEIPT` (reçu de commande) | Confirmation informelle d'achat — pas un document fiscal | **C'est exactement ce que produisent `Cart/Receipt.cshtml` et `Cart/OrderConfirmation.cshtml` aujourd'hui**, avec la mention explicite "Ce n'est pas une facture fiscale officielle." |
| `COMMERCIAL_INVOICE` (facture commerciale) | Document de facturation standard entre un vendeur et un acheteur, sans nécessairement toutes les mentions fiscales obligatoires (numéros de taxe, etc.) | **N'existe pas séparément aujourd'hui** — le reçu actuel joue partiellement ce rôle sans les mentions fiscales |
| `TAX_INVOICE`/`SUPPORTING DOCUMENT` (facture fiscale / pièce justificative) | Document conforme aux exigences fiscales (numéros TPS/TVQ, ventilation des taxes, mentions obligatoires) permettant à l'acheteur de réclamer un crédit de taxe le cas échéant | **N'existe pas** — et ne doit pas être improvisé tant que le statut d'inscription réel de Cosmechic (affirmations #6/#7) n'est pas connu |

### 9.2 `INVOICE_REQUIREMENT_MATRIX`

| FIELD | REQUIRED_WHEN | SOURCE | CURRENTLY_AVAILABLE | BUSINESS_INPUT_REQUIRED | LEGAL_CONFIRMATION_REQUIRED | IMPLEMENTATION_LOCATION |
|---|---|---|---|---|---|---|
| Nom légal du vendeur | Toujours, pour tout document se présentant comme facture (`COMMERCIAL_INVOICE` ou `TAX_INVOICE`) | Pratique standard + LPC (identification du commerçant, affirmation #2) | NON — `BusinessInformationOptions.LegalBusinessName` vide | OUI — dénomination légale exacte | OUI — doit correspondre à l'enregistrement réel | `BusinessInformationOptions.cs`, `Cart/Receipt.cshtml` |
| Adresse du vendeur | Idem | Idem | NON | OUI | OUI | Idem |
| Numéro de TPS | `TAX_INVOICE` uniquement, et seulement si Cosmechic dépasse le seuil de petit fournisseur (affirmation #6) ou s'inscrit volontairement | CRA — affirmation #6/#8 | NON | OUI — chiffre d'affaires réel pour déterminer l'obligation, puis numéro réel une fois inscrit | **OUI, préalable** — la question de savoir si Cosmechic doit même en avoir un n'est pas tranchée | `BusinessInformationOptions.TaxRegistrationNumbers`, reçu |
| Numéro de TVQ | Idem, pour Revenu Québec (affirmation #7) | Revenu Québec | NON | Idem | Idem | Idem |
| Ventilation séparée TPS/TVQ | `TAX_INVOICE` uniquement, une fois inscrit | Synthèse recoupée (affirmation #8) | NON — seul un `TaxAmount` agrégé est affiché ; les deux taux (5 % + 9,975 %) existent déjà séparément en base (`TaxRate`) | Aucune donnée business supplémentaire nécessaire (déjà en base) | OUI — confirmer l'exigence exacte de présentation | `Cart/Receipt.cshtml`, `TaxRate` (déjà présent) |
| Description des biens | `ORDER_RECEIPT` et au-delà | Déjà une bonne pratique universelle | **OUI** — nom produit, SKU, prix unitaire, quantité déjà affichés | Aucun | Non | `Cart/Receipt.cshtml` (déjà conforme) |
| Sous-total / Total / Taxes / Rabais / Remboursé | `ORDER_RECEIPT` et au-delà | Snapshot financier déjà persistant, jamais recalculé | **OUI** — déjà affiché, garanti par contrainte CHECK SQL | Aucun | Non | `OrderHeader`, `Cart/Receipt.cshtml` (déjà conforme) |
| Numéro de transaction/commande | `ORDER_RECEIPT` et au-delà | Pratique standard | **OUI** — `OrderHeader.Id` déjà affiché | Aucun | Non | Déjà conforme |
| Date | `ORDER_RECEIPT` et au-delà | Pratique standard | **OUI** — `OrderDate` déjà affichée | Aucun | Non | Déjà conforme |
| Nom de l'acheteur | `TAX_INVOICE`, requis ≥ 150 $ CAD selon la règle ITC citée (affirmation #8, à confirmer pour un contexte B2C) | Synthèse recoupée | **OUI** — `OrderHeader.Name` déjà affiché sur le reçu | Aucun | OUI — confirmer si la règle des 150 $ s'applique à un contexte B2C consommateur final (pas de CTI réclamé) | Déjà présent, à requalifier une fois le statut `TAX_INVOICE` confirmé |
| Mention "pas une facture fiscale officielle" | Tant que le statut `TAX_INVOICE` n'est pas atteint | Bonne pratique de transparence déjà adoptée par Cosmechic | **OUI**, déjà affichée sur `Cart/Receipt.cshtml` | Aucun | Formulation à faire réviser par un juriste, mais l'intention est correcte | Déjà en place |

---

## Section 10 — PM Decision Pack

```
PM_DECISION_PACK

DECISION_1_COSMETIC_OPENED_PRODUCTS
OPTION_A = Aligner strictement sur le minimum légal : aucune exclusion de la garantie légale
  de conformité (défaut/non-conformité/erreur toujours acceptés sans condition d'état
  d'ouverture), et traiter séparément le "remords" (où Cosmechic a pleine discrétion,
  ouvert ou non) — sans adopter d'exclusion spécifique aux produits ouverts pour le remords.
  LEGAL_RISK = Faible (strictement conforme aux droits impératifs identifiés).
  CUSTOMER_IMPACT = Politique perçue comme généreuse, mais accepter un retour "remords" sur un
    produit cosmétique ouvert crée un risque sanitaire/de revente pour Cosmechic elle-même.
  OPERATIONAL_IMPACT = Faible changement de code (aucune structuration de motif nécessaire
    dans l'immédiat) ; risque opérationnel de gestion des retours de produits usagés.
  RECOMMENDATION = Non formulée ici — décision du PM.

OPTION_B = Exclure explicitement le remords pour tout produit cosmétique descellé/ouvert
  (mais jamais pour un défaut/non-conformité/erreur Cosmechic, qui restent hors de portée de
  toute politique commerciale), avec divulgation claire avant achat conformément à l'art. 54.4.
  LEGAL_RISK = Faible à modéré — dépend directement de la confirmation de l'affirmation #4
    (incertaine) par un juriste ; si l'exclusion hygiène n'a pas d'équivalent confirmé en droit
    québécois, ce choix reste légalement possible **en tant que politique commerciale
    divulguée**, pas en tant qu'exclusion légale automatique.
  CUSTOMER_IMPACT = Plus restrictif, standard dans l'industrie cosmétique.
  OPERATIONAL_IMPACT = Nécessite un champ structuré "état déclaré à la demande" sur
    `ReturnItem` et une branche de règle dans `ReturnService.CanRequestReturnAsync`
    distinguant motif "remords" de motif "défaut/non-conformité/erreur".
  RECOMMENDATION = Non formulée ici — décision du PM.

PM_DECISION_REQUIRED = OUI, sur : (1) le choix A/B/autre, (2) la formulation exacte à
  divulguer avant achat, (3) confirmation juridique préalable de l'affirmation #4 avant de
  s'appuyer dessus. Distincte et indépendante : mise en place d'un canal de signalement de
  sécurité produit avec triage sous 2 jours (obligation fédérale potentielle, affirmation #5),
  quelle que soit l'option A/B retenue pour le retour lui-même.

DECISION_2_DATA_RETENTION
OPTION_A = Adopter le plancher légal identifié comme durée minimale pour toute donnée
  transactionnelle/fiscale (`OrderHeader`, `OrderDetail`, `Refund`) — 6 ans après la dernière
  année fiscale concernée (à confirmer : 6 ou 7 ans, écart non résolu par cette recherche),
  et ne définir aucune durée de suppression active au-delà de ce plancher (conservation
  indéfinie au-delà, comme c'est le cas aujourd'hui) sauf demande future explicite du PM.
  OPTION_B = Adopter le même plancher légal, ET définir une durée cible de purge/anonymisation
  au-delà pour les catégories non transactionnelles (paniers abandonnés, avis, historiques
  d'état) — nécessiterait un mécanisme de purge (scheduler), explicitement hors périmètre de
  ce lot, à traiter dans un lot d'implémentation ultérieur si retenu.
  RECOMMENDATION = Non formulée ici — décision du PM, une fois l'écart 6/7 ans confirmé par un
  comptable/fiscaliste.
  PM_DECISION_REQUIRED = OUI.

DECISION_3_INVOICE_TAX_INFO
INFORMATION_REQUIRED_FROM_OWNER = Chiffre d'affaires réel de Cosmechic (pour déterminer
  l'obligation d'inscription TPS/TVQ, affirmations #6/#7) ; si applicable, numéros de TPS et
  TVQ réels une fois inscrite ; nom légal, adresse légale, province d'immatriculation ;
  décision sur l'affichage d'une ventilation TPS/TVQ séparée sur les documents clients.
LEGAL_CONFIRMATION_REQUIRED = OUI — statut d'inscription obligatoire ou volontaire, et
  exigences exactes de mentions selon que Cosmechic vend uniquement à des consommateurs
  finaux (pas de CTI en jeu) ou pourrait avoir des clients B2B.
RECOMMENDATION = Ne rien afficher comme numéro fiscal tant que ces deux points ne sont pas
  confirmés ; garder la mention actuelle "pas une facture fiscale officielle" jusque-là.
```

---

## Section 11 — Aucune implémentation (confirmation)

`CODE_CHANGES_ALLOWED = NO`, respecté intégralement. Seul ce document
(`docs/audits/COSMECHIC-LEGAL-DECISION-RESEARCH-001.md`) a été créé. Aucun fichier applicatif
(`.cs`, `.cshtml`, `.csproj`) n'a été modifié. Aucune migration créée. Aucun modèle modifié.
Aucun service modifié. Aucune nouvelle politique codée. Aucun nettoyage des colonnes
`AspNetUsers` legacy. Aucune purge `ShoppingCart`.

---

## Section 12 — Rapport final

```
LOT=COSMECHIC-LEGAL-DECISION-RESEARCH-001
STATUS=COMPLETE
BASELINE_SHA=6fab329924a71e6fc98b3af1eb90d05af7f8059e
FINAL_SHA=(voir commit ci-dessous — seul ce document ajouté)
WORKTREE=CLEAN (avant commit), commit local unique ensuite
WARNING_COUNT_DISCREPANCY_RESOLVED=YES
WARNINGS_BASELINE=48 (résumé MSBuild, e1c4770, méthode clean+no-incremental+sln) / 46 (fingerprints uniques)
WARNINGS_CURRENT=48 (résumé MSBuild, 6fab329, méthode identique) / 46 (fingerprints uniques)
WARNING_FINGERPRINT_DELTA=0 — ensembles strictement identiques, prouvé par diff de worktree isolé
ASPNETUSERS_LEGACY_ADDRESS_GAP=CONFIRMÉ ET REQUALIFIÉ — classification ACTIVE (admin CRUD), non anonymisé, non exporté au client ; voir ASPNETUSERS_LEGACY_ADDRESS_MATRIX section 4.3
SHOPPING_CART_PRIVACY_GAP=CONFIRMÉ — paniers abandonnés/orphelins non couverts par l'anonymisation ; paniers anonymes prouvés inexistants ; voir SHOPPING_CART_PRIVACY_MATRIX section 5.4
COSMETIC_OPENED_PRODUCT_RESEARCH=COMPLET — 10 scénarios documentés, distinction centrale remords/discrétion vs non-conformité/défaut impératif établie, obligation fédérale de signalement de sécurité sous 2 jours identifiée comme point indépendant
DATA_RETENTION_RESEARCH=COMPLET — plancher comptable/fiscal de 6 ans (écart 6/7 ans non résolu, à confirmer) identifié comme contrainte plancher ; aucune durée choisie
INVOICE_TAX_RESEARCH=COMPLET — distinction ORDER_RECEIPT/COMMERCIAL_INVOICE/TAX_INVOICE établie ; seuil de petit fournisseur 30 000$ (TPS et TVQ) identifié comme préalable à toute décision de numéros fiscaux
OFFICIAL_SOURCES_COUNT=7 pages gouvernementales distinctes citées (via synthèse WebSearch — WebFetch bloqué pour tous les domaines .gouv.qc.ca/.canada.ca/.gc.ca testés dans cet environnement, limitation déclarée explicitement section 6.1)
UNRESOLVED_LEGAL_QUESTIONS=6 (numéros d'article exacts de la garantie légale LPC ; existence réelle au Québec d'une exclusion hygiène pour produits descellés — affirmation #4 incertaine ; écart 6 vs 7 ans de rétention fiscale ; rôle exact de Cosmechic sous la LCSPC — vendeur vs importateur ; applicabilité de la règle des seuils 30$/150$ à un contexte B2C sans CTI ; statut d'inscription TPS/TVQ réel de Cosmechic)
LEGAL_COUNSEL_CONFIRMATION_REQUIRED=YES (sur les 6 points ci-dessus)
PM_DECISIONS_REQUIRED=3 (plus deux gates de confidentialité additionnels : AspNetUsers legacy address, ShoppingCart orphan risk — voir sections 4/5, à trancher indépendamment des 3 décisions légales)
CODE_CHANGES=NONE
MIGRATIONS_CREATED=0
RESTORE=PASS
BUILD=PASS (0 erreur, 48 avertissements MSBuild / 46 fingerprints uniques — voir section 2)
ERRORS=0
TESTS=399/399 PASS
NUGET_VULNERABILITIES=0
MODEL_MIGRATION_DRIFT=NONE (CosmechicsContext et ApplicationDbContext)
PRODUCTION_TOUCHED=NO
REAL_STRIPE_USED=NO
REAL_EMAIL_SENT=NO
PUSHED=NO
LEGAL_READINESS=BLOCKED
PRODUCTION_RELEASE_AUTHORIZATION=BLOCKED
SAFE_TO_START_LEGAL_POLICY_IMPLEMENTATION_001=NO
```

**STOP.** En attente de la confrontation de ces conclusions aux sources officielles par le PM,
du challenge des trois recommandations, et des décisions finales — y compris sur les deux
gates de confidentialité (AspNetUsers legacy address, ShoppingCart orphan risk) désormais
traités comme conditions de clôture à part entière, indépendamment des trois politiques
légales. Aucun lot d'implémentation ne doit commencer avant cela.
