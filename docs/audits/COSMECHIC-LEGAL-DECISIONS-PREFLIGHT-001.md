# COSMECHIC-LEGAL-DECISIONS-PREFLIGHT-001

**Mode : ANALYSE / LECTURE SEULE.** Ce lot ne modifie aucune règle métier, aucune donnée,
aucun schéma. Il ne fait qu'inventorier des faits vérifiables dans le dépôt, pour permettre
au PM (propriétaire de Cosmechic) de prendre — avec Claude, mais sur la base de données
concrètes — les trois décisions légales encore ouvertes : `COSMETIC_OPENED_PRODUCT_RETURN_POLICY`,
`DATA_RETENTION_PERIODS`, `INVOICE_LEGAL_TAX_INFO`.

Aucune valeur juridique, fiscale ou de durée n'est inventée nulle part dans ce document.
Quand une case ne peut pas être remplie sans une décision du PM ou un avis juridique réel,
elle est marquée explicitement `DÉCISION_REQUISE` ou `QUESTION_JURIDIQUE` plutôt que comblée
par une supposition.

---

## Section 1 — Préflight

| Vérification | Résultat |
|---|---|
| HEAD attendu | `e1c4770e3eead6027947a40575363cfcf90ee2af` |
| HEAD constaté avant travaux | `e1c4770e3eead6027947a40575363cfcf90ee2af` — **conforme** |
| Worktree | `git status --porcelain` vide — **CLEAN** |
| Branche | conforme à la branche de travail désignée |
| Push effectué | NON |
| Production touchée | NON |
| Restore | PASS |
| Build (Release) | PASS — 0 erreur, 46 avertissements (baseline inchangée) |
| Tests | **399/399 PASS** |
| Vulnérabilités NuGet (`dotnet list package --vulnerable --include-transitive`) | **0** (3 projets : Cosmechic, Cosmechic.Utility, Cosmechic.Tests) |
| Dérive EF (`dotnet ef migrations has-pending-model-changes`) | `CosmechicsContext` → NONE ; `ApplicationDbContext` → NONE |
| Migrations créées ce lot | 0 |

Aucune divergence détectée. Le lot procède.

---

## Section 2 — Cadrage

Ce document ne choisit aucune politique. Il construit la matrice factuelle nécessaire pour
que le PM décide de A) `COSMETIC_OPENED_PRODUCT_RETURN_POLICY`, B) `DATA_RETENTION_PERIODS`,
C) `INVOICE_LEGAL_TAX_INFO`. Aucune hypothèse juridique n'est transformée en règle appliquée.

---

## Section 3 — Retours de produits cosmétiques (état d'ouverture)

### 3.1 Inventaire du code existant

| Élément | Emplacement | Constat factuel |
|---|---|---|
| `ReturnRequest` (modèle) | `Cosmechic/Models/ReturnRequest.cs` | `Id, OrderId, ApplicationUserId, Status, Reason, CustomerComment, CreatedAt, ApprovedAt, ReceivedAt, CompletedAt, AdminComment`. Aucun champ d'état du produit. |
| `ReturnItem` (modèle) | `Cosmechic/Models/ReturnItem.cs` | `Id, ReturnRequestId, OrderDetailId, Quantity, Reason` (texte libre nullable). **Aucun champ structuré** pour ouvert/non ouvert/utilisé/endommagé/défectueux/sale. |
| `ReturnService.CanRequestReturnAsync` | `Cosmechic/Services/ReturnService.cs:25-98` | Vérifie : commande expédiée/livrée, paiement confirmé, fenêtre de 30 jours (`CommercePolicyOptions.ReturnWindowDays`, approuvé COSMECHIC-BUSINESS-POLICY-001), quantité déjà réclamée. **Aucune vérification liée à l'état du produit** (ouvert, utilisé, scellé, hygiène). |
| `CreateReturnRequestAsync` | `Cosmechic/Services/ReturnService.cs:100-168` | Persiste `Reason`/`CustomerComment` en texte libre par ligne. Aucune branche selon un motif structuré. |
| `ReturnsController` | `Cosmechic/Controllers/ReturnsController.cs` | `RequestReturn` (GET), `RequestReturnSubmit` (POST), `Details` (GET). Vérifie la propriété de la commande (`ApplicationUserId` dérivé côté serveur). Ne collecte que texte libre. |
| `Views/Returns/Request.cshtml` | vue client | Formulaire : quantité par ligne + `Reason` (texte libre) + `Comment` (texte libre). **Aucun sélecteur d'état du produit, aucune case "produit ouvert/scellé".** |
| `Views/Home/Returns.cshtml` | page publique "Politique de retour" | Explique la fenêtre de 30 jours et le principe général. **Aucune mention d'un traitement différencié pour un produit cosmétique ouvert/utilisé.** |
| `OrderOperations/Details.cshtml` (admin) | `Cosmechic/Views/OrderOperations/Details.cshtml:78-135` | Boutons Approve/Reject pour une demande de retour. Le contrôleur accepte un `AdminComment`, mais **le formulaire ne rend actuellement aucun champ de saisie pour ce commentaire** — constat factuel, non corrigé ici (une correction changerait un comportement d'écran, hors du périmètre "erreur de documentation purement factuelle" de la section 8). |
| `RefundCause` (enum) | `Cosmechic/Services/RefundCause.cs` | `CustomerRemorse`, `MerchantFault` — choisi par l'admin **au moment du déclenchement du remboursement**, pas au moment de la demande de retour. N'affecte, par la politique déjà approuvée (BUSINESS-POLICY-001), que le remboursement des frais de livraison d'origine. **Ne détermine pas si le retour est accepté** ; ce n'est pas un mécanisme de classification de l'état du produit. |
| "Échange" (concept) | — | **N'existe nulle part dans le code** : aucun modèle, contrôleur, vue ou service ne représente un échange de produit. Seul le remboursement existe comme dénouement d'un retour approuvé. |

### 3.2 Ce que le logiciel peut représenter aujourd'hui

- Un client peut demander le retour de n'importe quelle ligne de commande expédiée/livrée,
  payée, dans les 30 jours, avec une explication en texte libre.
- Un administrateur peut Approuver ou Rejeter cette demande **manuellement**, en lisant le
  texte libre — aucune règle automatique ne différencie les scénarios ci-dessous.
- Une fois le retour `Received` puis `Completed`, un remboursement peut être déclenché avec
  une cause `CustomerRemorse` ou `MerchantFault`, qui influence uniquement le remboursement
  des frais de port d'origine (règle déjà approuvée, indépendante de l'état du produit).
- Il n'existe **aucun** mécanisme d'échange, de frais de restockage, de preuve photo
  obligatoire, de motif structuré, ou de règle spécifique aux produits cosmétiques.

### 3.3 `OPENED_PRODUCT_POLICY_DECISION_MATRIX`

| SCENARIO | CURRENT_BEHAVIOR | TECHNICAL_CAPABILITY | LEGAL_DECISION_REQUIRED | CODE_LOCATION | IMPLEMENTATION_IMPACT |
|---|---|---|---|---|---|
| Produit non ouvert (remords / changement d'avis) | Identique à tous les autres cas : demande texte libre → décision manuelle admin. Aucune distinction "scellé" n'existe. | Peut capturer "non ouvert" seulement si le client l'écrit en texte libre ; aucun champ dédié. | Le PM doit décider si Cosmechic accepte ce cas (remords) pour un produit scellé, et sous quelles conditions (délai, état de l'emballage). | `ReturnService.cs`, `ReturnItem.cs` | Si acceptée avec conditions différentes des autres cas, nécessite un champ structuré "état déclaré" + branche de règle dans `CanRequestReturnAsync` ou en révision admin. |
| Produit ouvert (sans usage allégué) | Identique — aucune distinction. | Idem ci-dessus. | Le PM doit décider si un produit cosmétique ouvert (donc non revendable en l'état) est éligible au retour/remboursement, et à quelles conditions. C'est la question centrale de `COSMETIC_OPENED_PRODUCT_RETURN_POLICY`. | `ReturnService.cs` | Idem — nouveau champ + règle si une politique différenciée est retenue. |
| Produit utilisé | Identique — aucune distinction structurée ; seul le texte libre peut le mentionner. | Idem. | Idem "produit ouvert", avec la question additionnelle de savoir si "utilisé" est traité différemment de "ouvert mais non utilisé". | `ReturnService.cs` | Idem. |
| Produit endommagé (à la réception / en transit) | Identique — traité comme toute autre demande, décision manuelle. | Le texte libre peut décrire le dommage ; aucune preuve (photo) n'est structurée ni requise par le système. | S'agit potentiellement d'un droit de non-conformité protégé (voir section 7) plutôt que d'une simple politique commerciale — à vérifier légalement, pas à décider seul par Cosmechic. | `ReturnService.cs`, `ReturnsController.cs` | Pourrait nécessiter un champ de preuve (upload photo) et un motif structuré distinct de "remords". |
| Défaut du produit (non-conformité) | Identique — aucune distinction entre "je ne l'aime plus" et "il est défectueux". | Idem. | Probable protection légale minimale non renonçable (voir section 7 — Loi sur la protection du consommateur, Québec) : à confirmer, jamais à supposer réglé par le simple silence du code actuel. | `ReturnService.cs` | Nécessite très probablement un motif structuré séparé (défaut vs remords) pour appliquer un traitement légalement différencié (p. ex. pas de frais, remboursement intégral incluant livraison). |
| Mauvais article expédié (erreur Cosmechic) | Identique — aucune distinction ; `RefundCause.MerchantFault` existe mais seulement au moment du remboursement, pas à la demande. | Le champ `RefundCause` peut déjà refléter "faute du marchand" une fois le remboursement déclenché — mais rien ne relie automatiquement "mauvais article" à ce choix ; c'est l'admin qui le choisit manuellement. | Le PM doit confirmer que ce cas relève toujours de `MerchantFault` (déjà la logique implicite attendue) et s'il doit être accéléré/prioritaire par rapport aux autres motifs. | `RefundOrchestrationService.cs`, `OrderOperations/Details.cshtml` | Impact limité — le mécanisme `RefundCause` existe déjà ; surtout une question de procédure admin, pas de nouveau code. |
| Erreur du marchand (cas général, hors mauvais article) | Identique. | Idem "mauvais article expédié". | Le PM doit définir le périmètre de "faute du marchand" au-delà de l'erreur d'expédition (p. ex. description produit trompeuse). | `RefundOrchestrationService.cs` | Aucun changement de code requis a priori — question de procédure/politique. |
| Réaction / allégation de sécurité (ex. réaction cutanée alléguée) | Identique — aucune voie de traitement spécifique, aucune escalade, aucun champ "signalement de sécurité". | Le texte libre peut porter cette information mais rien ne la distingue d'un simple motif de retour ni ne déclenche une alerte. | Question à la fois légale (obligations de sécurité des produits, rappels, responsabilité du fabricant/vendeur) et opérationnelle : le PM doit décider s'il faut un canal dédié (hors du flux de retour standard) et si un signalement doit être conservé différemment (voir section 4). | `ReturnsController.cs`, `HomeController.cs` (formulaire Contact, non persistant — voir section 4) | Pourrait nécessiter un canal ou une catégorie de signalement séparée de la demande de retour classique. |
| Produit rendu sale/souillé (à la réception du retour, si applicable) | Aucun champ ne permet de qualifier l'état du produit **reçu** par Cosmechic après retour ; le statut `Received` ne porte aucune métadonnée sur l'état constaté. | Le système peut enregistrer le passage à `Received` mais ne capture aucune observation qualitative à ce moment. | Le PM doit décider si un produit retourné dans un état jugé inacceptable (ouvert et visiblement utilisé, contaminé) peut donner lieu à un refus **après réception**, alors que la demande avait été initialement approuvée. | `ReturnService.MarkReceivedAsync`, `Cosmechic/Services/ReturnService.cs:176-177` | Nécessiterait un état intermédiaire ou un champ d'observation admin au moment de `MarkReceivedAsync`, actuellement inexistant. |
| Remboursement | Fonctionnel : `RefundOrchestrationService.RequestReturnRefundAsync` calcule marchandise/livraison/taxes ; frais de port remboursés seulement si `MerchantFault` (politique déjà approuvée) ; taxe remboursée proportionnellement (politique déjà approuvée). | Pleinement implémenté pour un retour déjà approuvé et reçu. | Aucune décision technique manquante pour le remboursement lui-même ; les décisions manquantes concernent uniquement **quand** un retour doit être approuvé selon l'état du produit (lignes ci-dessus). | `RefundOrchestrationService.cs` | Aucun. |
| Échange | **N'existe pas.** Aucun modèle, contrôleur, vue, service. | Aucune. | Le PM doit décider si un échange (plutôt qu'un remboursement) doit un jour exister comme fonctionnalité — actuellement hors périmètre technique complet. | — (fonctionnalité absente) | Développement complet requis si retenu ; non estimé ici (hors périmètre de ce lot d'analyse). |

**Constat central** : le système actuel ne fait techniquement **aucune différence** entre un
produit non ouvert, ouvert, utilisé, endommagé, défectueux ou allégué dangereux — tout passe
par le même champ de texte libre et la même décision manuelle Approve/Reject. La question
`COSMETIC_OPENED_PRODUCT_RETURN_POLICY` n'est donc pas seulement "quelle est la règle ?" mais
aussi, en amont, "le système a-t-il besoin d'un champ structuré pour appliquer une règle
différenciée, ou la discrétion admin actuelle suffit-elle ?" — question technique qui découle
directement de la décision légale/business, jamais l'inverse.

---

## Section 4 — Rétention des données (inventaire factuel)

Aucune durée n'est définie ci-dessous. Aucun scheduler, cron ou job n'existe dans ce dépôt
(confirmé par recherche exhaustive de `IHostedService`/`BackgroundService`/`Timer`/`Quartz`/
`Hangfire`/`cron` — zéro résultat) : **aucune purge automatique fondée sur le temps n'existe
nulle part dans ce code**, ce qui est un fait vérifiable, pas une supposition.

### 4.1 `DATA_RETENTION_MATRIX`

| DATA_CATEGORY | TABLE_OR_STORAGE | PERSONAL_DATA | PURPOSE | CURRENT_DELETION_BEHAVIOR | CURRENT_ANONYMIZATION_BEHAVIOR | RELATIONAL_DEPENDENCIES | LEGAL_RETENTION_DECISION_REQUIRED | TECHNICAL_PURGE_CAPABILITY | RISKS |
|---|---|---|---|---|---|---|---|---|---|
| Compte client (Identity) | `AspNetUsers` (`ApplicationDbContext`) | Oui — email, téléphone, hash de mot de passe, identifiants externes | Authentification, identité du compte | Jamais supprimé (hard-delete) par le code applicatif. | `AccountAnonymizationService.AnonymizeAsync` (déclenché uniquement à la demande explicite du client via `DeletePersonalData`) : email/username → `anon-{userId}@anonymized.invalid`, téléphone effacé, mot de passe détruit, verrouillage permanent, connexions externes retirées, 2FA désactivée. | Référencé (FK non nullable ou quasi) par `OrderHeader.ApplicationUserId`, `ReturnRequest.ApplicationUserId`, `Refund.RequestedByUserId`, `OrderStatusHistory.ActorUserId`, `StockMovement.ActorUserId`, `CustomerAddress.ApplicationUserId`, `ShoppingCart.ApplicationUserId`. | Combien de temps conserver la ligne anonymisée ; l'anonymisation actuelle suffit-elle légalement (Loi 25/PIPEDA) à considérer les données comme "supprimées". | Anonymisation sur demande : OUI. Suppression automatique programmée : NON (aucun scheduler). | **Champs fantômes `StreetAddress/City/State/PostalCode` sur `AspNetUsers` (voir 4.2) ne sont PAS effacés par `AnonymizeAsync`** — risque de PII résiduelle non couverte par l'anonymisation actuelle. |
| Adresses client (carnet d'adresses) | `CustomerAddress` | Oui — nom du destinataire, téléphone, adresse postale complète | Pré-remplissage checkout, gestion multi-adresses | `AnonymizeAsync` les **supprime intégralement** (hard-delete) — jugées non transactionnelles. | N/A (suppression directe). | FK vers `AspNetUsers` ; aucune référence entrante (rien ne dépend de `CustomerAddress`). | Aucune obligation de conservation identifiée a priori (pas un enregistrement transactionnel) — à confirmer par le PM/juridique. | Suppression déjà possible sur demande ; pas de purge automatique par ancienneté. | Aucun risque relationnel identifié (suppression déjà sûre, testée `AccountAnonymizationSqlServerTests.cs`). |
| Commande (en-tête) | `OrderHeader` | Oui — `Name`, `PhoneNumber`, `StreetAddress` (snapshot), `City/State/PostalCode/CountryCode` conservés délibérément | Historique transactionnel, preuve de vente, audit fiscal par juridiction | Jamais supprimée. | Anonymisation partielle : `Name → "Client anonymisé"`, `PhoneNumber → "0000000000"`, `StreetAddress → "[adresse anonymisée]"`. `City/State/PostalCode/CountryCode` **volontairement conservés** (justifiés comme nécessaires à l'audit de la taxe appliquée par juridiction). | Référencée par `OrderDetail`, `OrderStatusHistory`, `ReturnRequest`, `Refund` (via la commande), `StockMovement` (indirect). Snapshot financier protégé par contrainte CHECK (`OrderTotal = Subtotal + ShippingAmount + TaxAmount - DiscountAmount`). | Durée de conservation obligatoire pour finalités comptables/fiscales (probable, à confirmer — voir section 7) ; si une durée légale existe, elle doit primer sur toute anonymisation plus rapide. | Anonymisation partielle déjà en place ; **aucune suppression n'est techniquement possible sans risquer de casser l'historique comptable** (which est un choix architectural délibéré, pas une lacune). | Rétention illimitée de facto (pas de purge) — à valider comme conforme ou excessive selon les obligations réelles. |
| Détails de commande (lignes) | `OrderDetail` | Indirect (associé à une commande identifiée) | Détail des articles achetés, prix/quantité au moment de l'achat | Jamais supprimé. | Aucune (aucune donnée directement identifiante sur cette table). | FK vers `OrderHeader` (comportement de suppression : voir contrainte EF — aucune suppression n'est jamais déclenchée en pratique). | Suit la même décision que `OrderHeader` (formant un seul ensemble transactionnel). | Aucune purge. | Aucun risque additionnel au-delà de `OrderHeader`. |
| Demande de retour | `ReturnRequest` | Oui — `Reason`, `CustomerComment`, `AdminComment` (texte libre pouvant contenir des détails personnels ou médicaux, ex. allégation de réaction cutanée) | Trace du processus de retour/remboursement | Jamais supprimée. | Non couverte explicitement par `AccountAnonymizationService` — reste liée à `ApplicationUserId` (le compte est anonymisé, mais le texte libre de `Reason`/`CustomerComment`/`AdminComment` n'est ni expurgé ni examiné). | FK vers `OrderHeader`, `AspNetUsers` (`ApplicationUserId`). | Si `CustomerComment` peut contenir des données de santé (allégation de réaction), une politique de rétention/minimisation spécifique pourrait être requise. | Aucune purge, aucune expurgation du texte libre. | **Risque identifié** : texte libre non structuré pouvant contenir des données sensibles (santé), jamais anonymisé ni expurgé par le processus actuel. |
| Lignes de retour | `ReturnItem` | Indirect + `Reason` (texte libre par ligne) | Détail des articles retournés | Jamais supprimée. | Non couverte par l'anonymisation. | FK vers `ReturnRequest`, `OrderDetail`. | Idem `ReturnRequest`. | Aucune purge. | Idem `ReturnRequest`. |
| Remboursement | `Refund` | Indirect (montants, `Cause`), pas de PII directe hormis le lien `RequestedByUserId` | Trace financière du remboursement, preuve comptable | Jamais supprimé. | Non couvert explicitement — `RequestedByUserId` reste un FK vers le compte (anonymisé indirectement puisque le compte lui-même l'est). | FK vers `ReturnRequest`/`OrderHeader`, `AspNetUsers` (`RequestedByUserId`). Export personnel exclut déjà `StripeRefundId`/`IdempotencyKey`/`FailureCode`/`AdminComment` (BUSINESS-POLICY-001). | Probable obligation comptable/fiscale de conservation (preuve de remboursement) — à confirmer. | Aucune purge. | Aucun risque nouveau au-delà de ceux déjà traités par l'export IDOR-safe existant. |
| Historique de statut de commande | `OrderStatusHistory` | Indirect — `ActorUserId` (FK), commentaire libre éventuel | Piste d'audit des transitions de statut (qui a fait quoi, quand) | Jamais supprimé. | Non couvert par l'anonymisation (le FK `ActorUserId` reste vers un compte potentiellement anonymisé, mais la ligne elle-même n'est jamais modifiée). | FK vers `OrderHeader`, `AspNetUsers` (`ActorUserId`). | Piste d'audit — probablement à conserver aussi longtemps que la commande elle-même, mais c'est une décision, pas un fait acquis. | Aucune purge. | Aucun risque nouveau identifié au-delà du lien FK déjà neutralisé par l'anonymisation du compte acteur. |
| Mouvements de stock | `StockMovement` | Indirect — `ActorUserId` (FK) | Ledger d'audit des variations de stock (vente, restock, ajustement) | Jamais supprimé. | Non couvert — FK vers compte potentiellement anonymisé. | FK vers `AspNetUsers` (`ActorUserId`), potentiellement `Produit`/`OrderDetail`. | Donnée principalement opérationnelle/comptable (gestion d'inventaire) — rétention probablement liée aux obligations comptables générales plutôt qu'à la protection des renseignements personnels du client. | Aucune purge. | Risque limité — peu de PII directe (l'acteur est généralement un membre du personnel, pas un client). |
| Événements Stripe traités | `ProcessedStripeEvent` | **Non** — confirmé par lecture du modèle : aucun champ nominatif, uniquement des identifiants techniques d'événement pour garantir l'idempotence des webhooks. | Idempotence technique (empêcher le double traitement d'un même événement Stripe) | Jamais supprimé. | N/A (pas de PII). | Aucune dépendance relationnelle significative. | Probablement aucune (donnée technique, pas personnelle) — à confirmer si le PM considère l'ID d'événement comme indirectement identifiant (peu probable). | Aucune purge. | Croissance non bornée dans le temps (table qui grossit indéfiniment) — risque opérationnel/technique, pas un risque de protection des données. |
| Messages de contact | **Aucune table** — confirmé par lecture de `HomeController.cs` (actions `Contact` GET/POST) : le formulaire envoie un courriel via `IEmailSender`, **rien n'est persisté en base**. | N/A dans ce dépôt (le contenu existe seulement dans l'e-mail envoyé, hors du contrôle applicatif après envoi). | — | N/A (rien à supprimer côté application). | N/A. | N/A. | Si des courriels sont conservés côté fournisseur SMTP/boîte de réception, leur rétention est **hors du périmètre de ce code** — question à traiter au niveau de l'infrastructure de messagerie, pas de l'application. | Aucune (rien n'est stocké ici). | Aucun risque applicatif direct ; risque potentiel côté infrastructure e-mail externe, non auditable depuis ce dépôt. |
| Journaux techniques (logs applicatifs) | **Aucun sink persistant** — confirmé : aucun Serilog, aucun sink base de données, aucun Application Insights configuré. Seul `ILogger` par défaut (console/stdout) est utilisé. | Potentiellement — certains messages de log contiennent des identifiants (`userId`, `orderId`) mais jamais de mot de passe/PII directe (déjà audité en COSMECHIC-SECURITY-002). | Diagnostic/exploitation. | N/A — la rétention des logs dépend entièrement de l'infrastructure d'hébergement (durée de rétention du stdout/conteneur), **hors du contrôle de ce code**. | N/A. | N/A. | Hors périmètre applicatif — dépend de la configuration d'hébergement future (non couverte par ce lot, qui exclut explicitement la configuration de préproduction). | Aucune (rien n'est stocké par l'application elle-même). | Aucun risque applicatif direct identifié dans ce dépôt. |
| Journaux d'audit métier | `OrderStatusHistory` (déjà listé ci-dessus) sert de facto de journal d'audit métier pour les commandes. **Aucune autre table d'audit générique n'existe.** | — | — | — | — | — | — | — | Déjà couvert par la ligne `OrderStatusHistory` ci-dessus. |
| Témoignages / avis clients | `TemoignagesClient` (contrôleur `AvisController`) | Partiel — `Nom` est un champ texte libre saisi par le client, **sans FK vers `AspNetUsers`** (confirmé par lecture du modèle : `Id, Nom, Commentaire, Note, Date, ProduitId, Produit`). | Avis produit public | Jamais supprimé automatiquement (suppression manuelle admin uniquement, hors périmètre de ce lot). | Non couvert par `AccountAnonymizationService` — et **structurellement non liable** à un compte puisqu'il n'existe aucun FK vers `AspNetUsers` (déjà noté et volontairement exclu de l'export personnel en BUSINESS-POLICY-001, pour cette même raison : pas de FK fiable). | FK vers `Produit` uniquement. | Le champ `Nom` étant du texte libre non lié à un compte, une demande de suppression/anonymisation ne peut pas être automatisée de façon fiable (pas d'identifiant technique fiable reliant l'avis à un utilisateur) — question de politique/processus à trancher. | Aucune purge automatique ; suppression manuelle admin existante mais non spécifique à une demande de protection des données. | Risque déjà connu : impossible de garantir programматiquement qu'un avis appartient à tel compte, donc impossible d'automatiser sa suppression lors d'une demande d'anonymisation. |
| Panier | `ShoppingCart` | Oui — `ApplicationUserId` (nullable), lié à un produit et une quantité | Panier d'achat en cours | Non couvert explicitement par `AnonymizeAsync` (uniquement `CustomerAddress` et `OrderHeader` sont traités) — les lignes de panier restantes d'un compte anonymisé ne sont ni supprimées ni anonymisées par ce processus. | Non couvert. | FK vers `AspNetUsers` (`ApplicationUserId`, nullable), `Produit`. | Donnée transitoire par nature (panier en cours) — le PM doit décider si elle doit être purgée à l'anonymisation ou si son caractère non-identifiant (pas de données personnelles autres que le lien de compte) la rend négligeable. | Aucune purge automatique ; non traitée par le flux d'anonymisation existant. | **Risque identifié** : lignes de panier orphelines potentiellement laissées liées à un `ApplicationUserId` anonymisé après une demande de suppression de compte. |
| Profil Identity — champs fantômes | `AspNetUsers.StreetAddress/City/State/PostalCode` (shadow properties, `ApplicationDbContext`) | Oui, si historiquement peuplés — adresse postale | Hérité d'un ancien flux (commentaire de code : "jamais utilisées dans le checkout actuel") | Jamais supprimé/effacé. | **Non traité par `AccountAnonymizationService`** (confirmé par lecture ligne par ligne du service — seuls `Email/UserName/PhoneNumber/Password/Lockout/2FA/ExternalLogins` sont touchés). | Propriétés fantômes sur `AspNetUsers` — aucune table séparée. | Si ces champs contiennent encore des données historiques réelles, ils doivent être couverts par une future décision d'anonymisation. | Aucune. | **Risque déjà signalé ci-dessus (ligne "Compte client")** — PII potentiellement résiduelle non couverte par l'anonymisation actuelle. |

### 4.2 Constat transversal

- **Aucune donnée n'est jamais supprimée automatiquement par ancienneté** dans ce dépôt : fait
  vérifié par l'absence totale de toute forme de tâche planifiée.
- **Deux risques concrets identifiés** par cette analyse (jamais corrigés ici, seulement
  documentés, conformément à la section 8 de la directive) :
  1. Les champs fantômes `AspNetUsers.StreetAddress/City/State/PostalCode` ne sont pas
     couverts par `AccountAnonymizationService.AnonymizeAsync`.
  2. Les lignes `ShoppingCart` restantes ne sont pas couvertes par ce même processus.
- Aucune de ces observations n'a été corrigée dans ce lot : les corriger changerait le
  comportement de suppression/anonymisation des données, ce qui est une décision de politique
  de rétention — explicitement hors périmètre de ce lot en lecture seule.

---

## Section 5 — Facture / reçu / taxes

### 5.1 Inventaire des surfaces existantes

| Surface | Emplacement | Constat |
|---|---|---|
| Reçu de commande | `Cosmechic/Views/Cart/Receipt.cshtml` | Affiche : numéro de commande, date, statut de paiement, nom/adresse/téléphone client, lignes d'articles (nom, SKU, prix unitaire, quantité, total ligne), sous-total, livraison (avec méthode), taxes, rabais, total, montant remboursé le cas échéant. Déclare explicitement : *"Ce document est un reçu de commande. Ce n'est pas une facture fiscale officielle."* |
| Confirmation de commande | `Cosmechic/Views/Cart/OrderConfirmation.cshtml` | Affiche un sous-ensemble similaire (numéro de commande, lignes, sous-total, livraison, taxes, rabais, total) — **aucune mention "pas une facture fiscale"** sur cette page (elle n'a jamais prétendu être une facture non plus : aucun terme "facture" n'y apparaît). |
| Snapshot financier | `OrderHeader` (`Subtotal, ShippingAmount, TaxAmount, DiscountAmount, OrderTotal, RefundedAmount`) | Persisté au moment de la commande, jamais recalculé depuis les prix courants ; protégé par contrainte CHECK SQL Server (`OrderTotal = Subtotal + ShippingAmount + TaxAmount - DiscountAmount`). |
| Taux de taxe | `Cosmechic/Models/TaxRate.cs` | `TaxRateId, Jurisdiction, CountryCode, RegionCode, Rate, EffectiveFrom, EffectiveTo, IsActive`. Configuration actuelle (COMMERCE-OPERATIONS-001A) : TPS 5 % + TVQ 9,975 % pour le Québec (`CountryCode=CA, RegionCode=QC`), seule juridiction pour laquelle une valeur réelle a été saisie — **aucune juridiction non établie n'a de valeur inventée**. |
| Méthode de livraison | `Cosmechic/Models/ShippingMethod.cs` | `ShippingMethodId, Name, Description, Price, FreeShippingThreshold (TODO_REQUIRES_BUSINESS_CONFIGURATION), IsActive, EstimatedMinDays/MaxDays, SortOrder`. |
| Informations légales vendeur | `Cosmechic/Services/BusinessInformationOptions.cs` | `LegalBusinessName, BusinessAddress, TaxRegistrationNumbers` — **tous nullable, tous vides par défaut, jamais renseignés avec une valeur fictive.** `SupportEmail`/`SupportPhone` existent aussi (support client, pas fiscal). |

### 5.2 `INVOICE_LEGAL_INFORMATION_MATRIX`

| FIELD | CURRENT_VALUE_SOURCE | CURRENTLY_DISPLAYED | BUSINESS_VALUE_REQUIRED | LEGAL_VALIDATION_REQUIRED | SAFE_TO_IMPLEMENT_AFTER_CONFIRMATION |
|---|---|---|---|---|---|
| Nom légal de l'entreprise | `BusinessInformationOptions.LegalBusinessName` (vide par défaut) | NON | OUI — le PM doit fournir la dénomination sociale/légale exacte de Cosmechic. | OUI — doit correspondre à l'enregistrement réel de l'entreprise. | OUI, une fois la valeur fournie par le PM (aucun code à écrire, seule la configuration change). |
| Adresse d'entreprise | `BusinessInformationOptions.BusinessAddress` (vide) | NON | OUI — adresse légale/postale de l'entreprise. | OUI — doit être l'adresse enregistrée officiellement. | OUI, une fois fournie. |
| Numéro d'entreprise (NEQ ou équivalent) | Aucun champ dédié n'existe actuellement. | NON | À déterminer par le PM si applicable au Québec pour ce type d'entreprise. | OUI — numéro d'immatriculation officiel. | Nécessite d'ajouter un champ à `BusinessInformationOptions` avant affichage — **pas encore fait**, car sa nécessité même dépend d'une décision non prise. |
| Numéro d'inscription TPS | `BusinessInformationOptions.TaxRegistrationNumbers` (vide, champ générique) | NON | Le PM doit d'abord confirmer si Cosmechic est/doit être inscrite à la TPS (dépend du chiffre d'affaires — voir section 6/7 : seuil de petit fournisseur). | OUI — numéro réel attribué par l'ARC, jamais à inventer. | OUI une fois le numéro réel fourni ET la question de l'obligation d'inscription tranchée. |
| Numéro d'inscription TVQ | Idem (`TaxRegistrationNumbers`) | NON | Idem — dépend du même seuil de petit fournisseur (Revenu Québec). | OUI — numéro réel attribué par Revenu Québec. | Idem. |
| Ventilation TPS / TVQ séparée sur le reçu | `TaxRate.Jurisdiction/Rate` existent en base (5 % + 9,975 % pour QC) mais le reçu n'affiche actuellement qu'un seul montant agrégé `TaxAmount`. | Agrégé seulement — PAS de ventilation ligne par ligne TPS vs TVQ affichée. | Le PM doit confirmer si une ventilation distincte est requise sur les documents remis aux clients (courant pour les factures fiscales conformes au Québec). | OUI — c'est une exigence potentiellement légale (mention distincte des taxes), pas seulement un choix d'affichage. | Techniquement réalisable (les deux taux existent déjà en base de façon distincte), mais nécessite de confirmer d'abord l'exigence légale avant d'ajouter l'affichage. |
| Mention "reçu, pas une facture fiscale officielle" | Texte statique dans `Cart/Receipt.cshtml` | OUI, déjà affiché. | Rester affichée tant que les informations ci-dessus ne sont pas complètes et validées. | Cette mention elle-même devrait être revue par un juriste pour confirmer sa formulation exacte, mais son intention (ne pas revendiquer une conformité non prouvée) est déjà correcte. | Déjà en place — aucune action requise avant la levée des points ci-dessus. |
| Montants (sous-total, livraison, taxes, rabais, total, remboursé) | `OrderHeader` (snapshot financier persisté) | OUI, déjà affichés sur reçu et confirmation. | Aucune valeur additionnelle requise — déjà fondée sur des données réelles de la commande. | Aucune — ce sont des montants réels de transaction, pas des mentions légales fiscales. | Déjà conforme. |
| Autres mentions obligatoires (ex. conditions de retour légalement requises sur facture, coordonnées de support) | `SupportEmail` (déjà configuré, réel — reflète `Smtp:FromAddress`) affiché sur les pages institutionnelles, pas sur le reçu lui-même actuellement. | Partiel (support affiché ailleurs, pas sur le reçu). | Le PM doit confirmer si d'autres mentions doivent apparaître spécifiquement sur le reçu/la facture (ex. politique de retour résumée, coordonnées de support). | OUI si une mention est légalement obligatoire sur une facture au Québec. | Dépend entièrement du résultat de la validation légale — rien à implémenter avant cette confirmation. |

**Aucune valeur de ce tableau n'a été inventée** : chaque case "NON affiché" reflète l'état
réel du code lu, et chaque case "requiert validation" reste ouverte plutôt que remplie par une
supposition.

---

## Section 6 — `PM_INFORMATION_REQUIRED`

Informations que seul le propriétaire réel de Cosmechic peut fournir — aucune valeur ci-dessous
n'est inventée ou supposée par cette analyse :

1. Dénomination sociale/nom légal exact de l'entreprise (`LegalBusinessName`).
2. Nom commercial, si différent du nom légal.
3. Adresse légale/postale complète de l'entreprise (`BusinessAddress`).
4. Province/juridiction d'immatriculation effective de l'entreprise.
5. Numéro d'entreprise du Québec (NEQ), si applicable.
6. Le chiffre d'affaires de Cosmechic dépasse-t-il (ou dépassera-t-il) le seuil de "petit
   fournisseur" fédéral et provincial rendant l'inscription à la TPS/TVQ obligatoire ? (Sans
   cette information, il est impossible de savoir si des numéros d'inscription doivent même
   exister.)
7. Si inscrite : numéro d'inscription TPS réel (attribué par l'ARC).
8. Si inscrite : numéro d'inscription TVQ réel (attribué par Revenu Québec).
9. Coordonnées de support client destinées à apparaître sur les documents légaux/factures, si
   différentes de `SupportEmail`/`SupportPhone` déjà configurés.
10. Politique souhaitée pour les retours de produits cosmétiques ouverts/utilisés : Cosmechic
    accepte-t-elle ces retours, sous quelles conditions (délai différent, frais, exclusion
    totale sauf défaut/erreur), et sur quelle base (hygiène, revente impossible) ?
11. Position de Cosmechic sur les réclamations de défaut/non-conformité et d'erreur
    d'expédition : accepte-t-elle déjà d'y appliquer un traitement plus favorable (remboursement
    intégral incluant livraison, sans condition de délai standard) ? (Utile pour confirmer que
    l'intention business rejoint l'obligation légale probable — voir section 7.)
12. Durées de conservation souhaitées (si le PM en a déjà une préférence business, à confronter
    ensuite à toute obligation légale identifiée) pour : comptes clients anonymisés, adresses,
    commandes/factures, retours/remboursements, avis clients.
13. Existence ou non d'une obligation contractuelle/légale déjà connue du PM imposant une durée
    de conservation minimale des documents comptables/fiscaux (ex. exigences de l'ARC/Revenu
    Québec en matière de conservation des registres).
14. Si un canal de signalement de sécurité produit (réaction cutanée alléguée, etc.) distinct du
    formulaire de retour standard est souhaité.

---

## Section 7 — Portée de la recherche juridique

Ce document **ne fournit aucun avis juridique** et ne prétend à aucune conclusion définitive.
Il identifie, à partir de faits du dépôt, les catégories de textes/régulateurs qu'une révision
juridique réelle devra couvrir, en séparant explicitement la nature de chaque affirmation.

| # | Sujet | FACT_FROM_REPOSITORY | BUSINESS_DECISION | LEGAL_QUESTION | TECHNICAL_IMPLEMENTATION |
|---|---|---|---|---|---|
| 1 | Droit de retour pour non-conformité/défaut | Le code ne différencie techniquement pas "défaut" de "remords" — fait vérifié en section 3. | Cosmechic doit décider de sa politique commerciale de base (délais, conditions) pour les retours volontaires. | La *Loi sur la protection du consommateur* (Québec) et le Code civil du Québec prévoient-ils des droits de garantie/non-conformité non renonçables par contrat, indépendamment de toute politique commerciale ? | Si oui, `ReturnItem`/`ReturnService` doivent probablement distinguer structurellement "défaut allégué" de "remords", pour ne jamais soumettre le premier aux mêmes limites (délai de 30 jours, etc.) que le second. |
| 2 | Retour de produits cosmétiques ouverts | Aucune règle d'exception hygiène n'existe dans le code (section 3). | Cosmechic doit décider si elle refuse par principe les retours de produits ouverts, sauf défaut/erreur. | Une telle exclusion est-elle licite dans le contexte québécois pour des produits cosmétiques, et si oui à quelles conditions (mention claire au moment de l'achat, etc.) ? | Si la politique diffère selon l'état déclaré, un champ structuré devient nécessaire (voir section 3.3). |
| 3 | Inscription et perception de la TPS/TVQ | `TaxRate` contient déjà 5 % + 9,975 % pour le Québec (COMMERCE-OPERATIONS-001A) — fait vérifié, mais **rien ne confirme que Cosmechic est effectivement inscrite** aux deux régimes ; aucun numéro d'inscription n'est configuré (`BusinessInformationOptions.TaxRegistrationNumbers` vide). | Le PM doit fournir son chiffre d'affaires réel pour situer l'entreprise par rapport au seuil de petit fournisseur. | Les règles d'inscription obligatoire à la TPS (loi fédérale) et à la TVQ (loi québécoise) au-delà du seuil de petit fournisseur, et les obligations de facturation qui en découlent (mentions obligatoires, numéros affichés) doivent être confirmées par un professionnel. | Si l'inscription est confirmée, les champs manquants identifiés en section 5.2 (numéros TPS/TVQ, ventilation) doivent être ajoutés à `BusinessInformationOptions` et au reçu. |
| 4 | Conservation des registres comptables/fiscaux | `OrderHeader`/`Refund` ne sont jamais supprimés par le code actuel (fait vérifié, section 4). | Aucune — c'est une obligation externe, pas un choix business. | Quelle est la durée minimale de conservation des registres de vente et de taxe exigée par l'ARC et Revenu Québec pour ce type d'activité ? | Aucune purge ne doit être implémentée avant que cette durée minimale soit connue, pour ne jamais supprimer une donnée encore légalement requise. |
| 5 | Protection des renseignements personnels | `AccountAnonymizationService` anonymise sur demande, mais laisse des champs non couverts (section 4.2) — fait vérifié. | Aucune — les obligations de protection des renseignements personnels sont externes. | La *Loi 25* (Québec) et la *LPRPDE/PIPEDA* (fédérale) définissent des obligations de minimisation, de durée de conservation proportionnée à la finalité, et de droit à la suppression/anonymisation — dans quelle mesure l'anonymisation actuelle (plutôt qu'une suppression complète) satisfait-elle ces obligations pour chaque catégorie de données de la section 4 ? | Selon la réponse, les deux lacunes déjà identifiées (champs fantômes `AspNetUsers`, lignes `ShoppingCart` orphelines) devront être corrigées, et potentiellement d'autres catégories révisées. |
| 6 | Données de santé alléguées (réactions cutanées, etc.) capturées en texte libre | `ReturnRequest.CustomerComment`/`ReturnItem.Reason` peuvent contenir ce type de contenu sans qu'aucun traitement spécial ne soit appliqué — fait vérifié (section 3/4). | Cosmechic peut vouloir un canal séparé pour ce type de signalement, indépendamment de toute obligation légale. | Ce type de donnée bénéficie-t-il d'une protection renforcée (donnée sensible) sous la Loi 25, justifiant un traitement/une rétention différents des commentaires de retour ordinaires ? | Si oui, nécessite probablement une classification distincte et une politique de rétention/accès plus stricte que le reste du champ texte libre. |
| 7 | Mentions obligatoires sur les documents remis au client | Le reçu affiche déjà un avertissement explicite qu'il n'est pas une facture fiscale officielle (fait vérifié, `Cart/Receipt.cshtml:82`). | Aucune — dépend des exigences légales, pas d'une préférence business. | Quelles mentions précises rendent un document "facture fiscale conforme" au sens des règles fédérales/québécoises applicables au commerce de détail en ligne ? | Aucune implémentation avant que la liste exacte des mentions obligatoires soit confirmée — éviter d'ajouter des mentions partielles qui donneraient une fausse impression de conformité. |

**Compteurs** :
- `LEGAL_QUESTIONS_COUNT` = 7 (une par ligne ci-dessus).
- `BUSINESS_DECISIONS_COUNT` = 6 (une par ligne, hors la ligne 4 qui n'en comporte aucune —
  purement une obligation externe).

---

## Section 8 — Changements de code

**`CODE_CHANGES = NONE`.**

Aucun fichier de production n'a été modifié dans ce lot. Seule la lecture, l'exécution de
commandes de vérification (`git status`, `dotnet restore/build/test/list package`,
`dotnet ef migrations has-pending-model-changes`) et la rédaction de ce document ont eu lieu.
Aucune des observations de risque (section 4.2) n'a été corrigée : les corriger changerait un
comportement de suppression/anonymisation de données, ce qui constitue une décision de
politique de rétention — explicitement hors du périmètre "erreur de documentation purement
factuelle" autorisé par la directive.

---

## Section 9 — Rapport final

```
LOT: COSMECHIC-LEGAL-DECISIONS-PREFLIGHT-001
STATUS: COMPLETE
BASELINE_SHA: e1c4770e3eead6027947a40575363cfcf90ee2af
FINAL_SHA: (voir commit ci-dessous — seul docs/audits/COSMECHIC-LEGAL-DECISIONS-PREFLIGHT-001.md ajouté)
WORKTREE: CLEAN (avant commit), commit unique local ensuite
COSMETIC_OPENED_PRODUCT_RETURN_POLICY: NON DÉCIDÉE — matrice factuelle fournie (section 3.3), aucune règle codée ni inventée
DATA_RETENTION_PERIODS: NON DÉCIDÉES — inventaire complet fourni (section 4.1), aucune durée définie, aucune purge créée
INVOICE_LEGAL_TAX_INFO: NON DÉCIDÉE — matrice factuelle fournie (section 5.2), aucune valeur fiscale/légale inventée
OPENED_PRODUCT_POLICY_DECISION_MATRIX: fournie (section 3.3, 11 scénarios)
DATA_RETENTION_MATRIX: fournie (section 4.1, 15 catégories de données)
INVOICE_LEGAL_INFORMATION_MATRIX: fournie (section 5.2, 9 champs)
PM_INFORMATION_REQUIRED: fournie (section 6, 14 éléments)
LEGAL_QUESTIONS_COUNT: 7
BUSINESS_DECISIONS_COUNT: 6
IMPLEMENTATION_ITEMS_BLOCKED: 3 (les trois décisions PM elles-mêmes) + éléments dépendants identifiés dans les matrices (champs structurés de retour, champs légaux facture, corrections d'anonymisation)
CODE_CHANGES: NONE
MIGRATIONS_CREATED: 0
RESTORE: PASS
BUILD: PASS (0 erreur, 46 avertissements — baseline inchangée)
ERRORS: 0
TESTS: 399/399 PASS
NUGET_VULNERABILITIES: 0
MODEL_MIGRATION_DRIFT: NONE (CosmechicsContext et ApplicationDbContext)
PRODUCTION_TOUCHED: NO
REAL_STRIPE_USED: NO
REAL_EMAIL_SENT: NO
PUSHED: NO
LEGAL_READINESS: BLOCKED
PRODUCTION_RELEASE_AUTHORIZATION: BLOCKED
SAFE_TO_BEGIN_IMPLEMENTATION: NO — seulement après que le PM ait explicitement tranché tout ou partie des décisions listées dans ce document, dans le cadre d'un lot ultérieur strictement limité (COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001) implémentant uniquement ce qui aura été approuvé.
```

**STOP.** Ce lot s'arrête ici, dans l'attente des décisions du PM sur les trois points
ci-dessus, avant tout lot d'implémentation.
