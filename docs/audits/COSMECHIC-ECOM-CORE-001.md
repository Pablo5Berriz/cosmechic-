# COSMECHIC-ECOM-CORE-001 — Paiement sécurisé, fulfillment de commande, cohérence du stock et idempotence Stripe

- **Lot** : COSMECHIC-ECOM-CORE-001
- **Base de départ** : `59e01f5c6cddd89d95e46f214f09099b7a30f1a5` (COSMECHIC-DATA-001)
- **Portée** : cœur transactionnel e-commerce — panier, checkout, webhook Stripe, idempotence, concurrence de stock, états de commande.
- **Hors scope, volontairement non touché** : EMAIL-001, bug de recherche produit, upload security (SEC-007), vulnérabilités NuGet, doublon `StripeSettings` (ARCH-001, sauf ajout minimal de `WebhookSecret`), refonte UI/SEO/accessibilité, moteur de livraison/taxes/retours, architecture DbContext/Identity.

## 1. Architecture avant / après

### Avant
```
Cart (ajout = Stock -= quantity, définitif)
  → SummaryPOST (recalcule le total, mais le formulaire lie OrderHeader en entier —
     surface de sur-liaison OrderTotal/PaymentStatus/OrderStatus/SessionId/
     PaymentIntentId/ApplicationUserId, neutralisée seulement par des écrasements a
     posteriori fragiles, sauf OrderTotal qui utilisait `+=` au lieu de `=`)
  → new SessionService().Create(...) dispersé dans le controller
  → navigateur redirigé vers Stripe
  → callback navigateur sur OrderConfirmation (GET)
  → OrderConfirmation interroge Stripe (session.PaymentStatus), et SI "paid" :
     marque PaymentStatus=Approved, OrderStatus=Approved, vide le panier —
     entièrement déclenché par la visite de la page, jamais par Stripe lui-même
  → aucun webhook, aucune vérification de signature, aucune idempotence
```

### Après
```
Cart (ajout = validation de quantité + création/mise à jour de la ligne ; Stock inchangé)
  → CheckoutService (seule source de vérité) : recalcule produits/prix/total depuis
     la base, crée une commande Pending, crée la session Stripe via
     IStripeCheckoutService, associe la commande via OrderId en metadata Stripe
  → navigateur redirigé vers Stripe
  → OrderConfirmation (GET) : vue d'état pure — lit la commande, vérifie l'ownership,
     n'appelle plus jamais Stripe, ne mute plus rien
  → Stripe → POST /webhooks/stripe (StripeWebhookController)
     - lit le payload brut + Stripe-Signature, vérifie via EventUtility.ConstructEvent
     - signature invalide → 400, aucune mutation
     - événement non supporté → 200, ignoré
     - délègue à StripeFulfillmentService
  → StripeFulfillmentService (transactionnel, idempotent) :
     - barrière n°1 : ProcessedStripeEvent.StripeEventId UNIQUE (idempotence)
     - barrière n°2 : commande déjà payée → ignorée (protège contre un 2e event id
       distinct pour le même paiement)
     - validation montant/devise contre OrderTotal calculé côté serveur
     - décrément de stock avec retry borné sur conflit RowVersion
     - transition d'état, nettoyage du panier, uniquement si tout réussit
```

## 2. Paiement

Le navigateur n'est plus jamais la source de vérité d'un paiement. `CheckoutService`
(`Cosmechic/Services/OrderCheckoutService.cs`) recalcule systématiquement, depuis la
base de données, tout ce qui constitue une valeur financière — produit, prix unitaire,
quantité, total — au moment du checkout. Les coordonnées de livraison soumises par le
client transitent par un DTO étroit (`ShippingAddress` : Nom/Téléphone/Adresse/Ville/
Province/Code postal uniquement) ; `OrderTotal`, `Price`, `PaymentStatus`,
`OrderStatus`, `SessionId`, `PaymentIntentId` et `ApplicationUserId` ne sont plus jamais
lus depuis une valeur soumise par le client — ils sont exclusivement calculés/attribués
à l'intérieur du service, à partir de données serveur (base de données, réponse Stripe
authentique, identité authentifiée).

Cela corrige une vraie faille de sur-liaison (mass assignment) présente dans
l'ancien code : `[BindProperty] ShoppingCartVM ShoppingCartVM` liait `OrderHeader` en
entier depuis le POST, et `OrderHeader.OrderTotal` utilisait `+=` au lieu de `=` —
un attacker pouvait donc injecter une valeur `OrderHeader.OrderTotal` arbitraire dans le
corps de la requête (même si la vue ne rendait aucun champ pour elle), corrompant le
total stocké en base (le montant réellement facturé par Stripe restait, lui, correct,
car construit indépendamment depuis les lignes de panier). Les autres champs
(`PaymentStatus`, `OrderStatus`, `SessionId`, `PaymentIntentId`, `ApplicationUserId`)
étaient déjà neutralisés par des écrasements a posteriori dans l'ancien code, mais de
façon fragile ; le nouveau design élimine la classe de vulnérabilité entièrement plutôt
que de la neutraliser au cas par cas.

## 3. Stock

- **Ajout au panier** (`ProduitsController.ItemDetails` POST) : ne décrémente plus
  jamais `Produit.Stock`. Valide la quantité demandée (`CartQuantityPolicy` :
  0 < quantité ≤ 50) et vérifie la disponibilité de façon purement informative (compare
  au stock actuel, sans le réserver).
- **Retrait/abandon de panier** : aucune mutation de stock associée — confirmé par
  inventaire complet (section 9 ci-dessous).
- **Fulfillment** (paiement webhook confirmé) : seul point de décrément réel,
  `StripeFulfillmentService.cs`, protégé par vérification de disponibilité + jeton de
  concurrence optimiste `RowVersion` (préparé par COSMECHIC-DATA-001).

## 4. États

Constantes existantes (`Cosmechic.Utility.SD`), non renommées, non dupliquées :

```
CURRENT_ORDER_STATUSES   = Pending, Approved, Processing, Shipped, Cancelled, Refunded
CURRENT_PAYMENT_STATUSES = Pending, Approved, ApprovedForDelayedPayment, Rejected
```

Convention retenue pour le nouveau flux (réutilise les constantes existantes, n'en
ajoute aucune) :

| Transition | OrderStatus | PaymentStatus |
|---|---|---|
| Commande créée (checkout) | `Pending` | `Pending` |
| Paiement confirmé + fulfillment réussi | `Processing` (`StatusInProcess`) | `Approved` |
| Paiement confirmé mais stock indisponible au fulfillment | `Pending` (inchangé) | `Approved` |
| Paiement échoué/annulé (webhook) | `Cancelled` | `Rejected` |

La combinaison `PaymentStatus=Approved` + `OrderStatus=Pending` est le signal explicite
et volontaire d'un besoin de remédiation administrative (section 8) — jamais un état
cascade automatique. `Shipped`/`Refunded` restent des transitions manuelles admin,
inchangées, hors scope de ce lot.

## 5. Transaction et concurrence

`StripeFulfillmentService.FulfillWithStockConcurrencyAsync` charge les lignes de
commande, vérifie la disponibilité du stock, décrémente, met à jour les statuts, vide
le panier de l'utilisateur et marque l'événement Stripe traité — le tout dans un seul
appel `SaveChangesAsync()`, atomique par construction (EF Core enveloppe un seul
`SaveChanges` dans une transaction implicite), enveloppé en plus dans une transaction
explicite (`Database.BeginTransactionAsync`) pour plus de clarté.

En cas de conflit de concurrence optimiste (`DbUpdateConcurrencyException`, deux
fulfillments concurrents sur le même produit), jusqu'à 3 tentatives sont effectuées :
les entités `Produit` en conflit sont rechargées (`ReloadAsync`) pour obtenir la valeur
réelle de `Stock`/`RowVersion`, puis la vérification de disponibilité est refaite avant
de retenter. Si le stock s'avère insuffisant (au premier essai ou après rechargement),
aucune mutation n'a lieu et le résultat est traité comme le cas « paiement confirmé mais
stock indisponible » (section 4/8) — jamais une commande fulfillie deux fois, jamais un
stock négatif.

**Vérifié contre un vrai SQL Server jetable** (pas seulement InMemory, qui n'applique ni
`RowVersion` ni contrainte `UNIQUE` avec la même sémantique — voir section 10) :
- Stock=1, deux commandes différentes tentent un fulfillment concurrent du même
  produit → exactement une réussit, l'autre échoue proprement, stock final = 0, jamais
  négatif.
- Une commande à deux lignes dont une seule a un stock insuffisant → **aucune** des deux
  lignes n'est mutée (atomicité), même celle qui avait assez de stock.

## 6. Webhook Stripe

`Cosmechic/Controllers/StripeWebhookController.cs`, `POST /webhooks/stripe` :
1. Lit le corps brut de la requête (jamais de binding de modèle).
2. Vérifie la signature via `Stripe.EventUtility.ConstructEvent(json, Stripe-Signature,
   secret)`. Secret lu depuis la configuration (`Stripe:WebhookSecret`), jamais en dur.
3. Signature invalide → `400`, aucune mutation, aucun `ProcessedStripeEvent` créé.
4. Type d'événement non supporté → `200` (accusé réception sans traitement).
5. Événement supporté délégué à `StripeFulfillmentService`.
6. Toute issue métier (déjà traité, commande introuvable, montant/devise incohérents,
   stock indisponible) retourne `200` — ce sont des décisions définitives déjà
   enregistrées, rejouer l'événement ne changerait rien (évite des retries Stripe
   inutiles). Seule une exception non gérée remonterait en `500`, invitant Stripe à
   retenter (cas transitoire).

**Événements supportés** (mode Stripe Checkout `payment`, méthodes instantanées) :
`checkout.session.completed`, `checkout.session.async_payment_succeeded`,
`checkout.session.async_payment_failed`. Choix documenté : Cosmechic n'utilise
actuellement que des méthodes de paiement instantanées côté Checkout ; les événements
asynchrones sont néanmoins supportés par prudence (méthodes différées possibles à
l'avenir) sans complexifier le système au-delà de ce qui est nécessaire.

**Bug de production corrigé au passage** : `EventUtility.ConstructEvent` a par défaut
`throwOnApiVersionMismatch: true`, qui aurait rejeté (avec l'erreur classée à tort comme
« signature invalide ») tout événement Stripe réel dont la version d'API du compte
diffère de celle intégrée au SDK Stripe.net installé — un scénario parfaitement normal
en production, indépendant de toute attaque. Corrigé en passant explicitement
`throwOnApiVersionMismatch: false`, la vérification de signature elle-même restant
strictement obligatoire.

## 7. Idempotence

Deux barrières indépendantes, requises ensemble par le mandat :

1. **`ProcessedStripeEvent.StripeEventId` UNIQUE** (préparé par COSMECHIC-DATA-001) :
   protège contre la redélivrance du même `StripeEventId`. Implémentation en deux
   temps : une vérification rapide (`AnyAsync`, agnostique du fournisseur) pour le cas
   courant de redélivrance séquentielle, puis une tentative d'`INSERT` dont l'échec par
   violation de contrainte `UNIQUE` (SQL Server, codes 2627/2601) est la garantie réelle
   contre deux requêtes concurrentes passant toutes deux la vérification rapide avant
   qu'aucune n'ait committé. **Vérifié contre un vrai SQL Server jetable** : deux appels
   concurrents avec le même `StripeEventId` → un seul enregistrement
   `ProcessedStripeEvent`, un seul effet métier (stock décrémenté une fois, pas deux).
2. **Commande déjà payée** (`OrderHeader.PaymentStatus == Approved`) : protège contre
   deux `StripeEventId` *distincts* référençant le même paiement (ex.
   `checkout.session.completed` puis `checkout.session.async_payment_succeeded` pour la
   même session) — la contrainte `UNIQUE` seule ne suffirait pas ici, puisque les
   identifiants d'événement diffèrent.

InMemory ne reproduisant pas le comportement d'un index `UNIQUE` (vérifié
empiriquement : deux `SaveChanges` distincts sur la même valeur logiquement unique ne
lèvent aucune exception), la barrière n°1 n'est prouvée de façon probante que contre un
vrai SQL Server (section 5/10) ; les tests InMemory couvrent la barrière n°2 et la
logique de branchement.

## 8. Cas difficile : paiement confirmé, stock indisponible au fulfillment

Documenté explicitement (mandat section 20), pas caché, pas résolu par un système de
réservation complexe (mandat section 21, volontairement non implémenté — volume actuel
de Cosmechic ne le justifie pas) : si le stock devient insuffisant entre la création de
la session Stripe et le traitement du webhook, le paiement est reconnu comme réellement
effectué (`PaymentStatus = Approved`, `PaymentIntentId`/`PaymentDate` enregistrés) mais
`OrderStatus` reste `Pending` au lieu de passer à `Processing`. Cette combinaison — payé
mais toujours en attente — EST le signal explicite qu'une remédiation administrative
(remboursement ou réapprovisionnement) est nécessaire ; `ProcessedStripeEvent.
ProcessingStatus` porte le détail exact (`Processed_StockUnavailable` ou
`Processed_ConcurrencyExhausted`).

## 9. Inventaire des mutations de `Produit.Stock`

| Emplacement | Déclencheur | Autorisé ? | Raison |
|---|---|---|---|
| `Cosmechic/Services/StripeFulfillmentService.cs:183` | Fulfillment webhook vérifié (idempotent, montant/devise validés, stock vérifié, concurrence RowVersion gérée) | **Oui** | Seul point légitime de consommation réelle du stock |
| `ProduitsController.Create`/`Edit` (admin, liaison de formulaire) | Action admin CRUD, préexistante, non modifiée par ce lot | Oui (hors scope, inchangé) | Ajustement manuel de stock par un administrateur |
| `ProduitsController.ItemDetails` POST (ajout au panier) | — | **Supprimé** | Était le bug ciblé par ce lot (section 3) ; confirmé absent après le changement |

Recherche exhaustive (`grep -rn "\.Stock\s*[+\-]?="`) : un seul site de mutation
arithmétique (`+=`/`-=`) dans tout le dépôt, celui du fulfillment ci-dessus.

## 10. Gap Identity confirmé (hérité de COSMECHIC-DATA-001, non corrigé ici)

COSMECHIC-DATA-001 avait *flaggé* — sans le vérifier en conditions réelles — que le
schéma Identity de base (migration `ApplicationDbContext`) ne contient pas les 4
colonnes `StreetAddress`/`City`/`State`/`PostalCode` que `CosmechicsContext.AspNetUser`
mappe pourtant. Ce lot **confirme concrètement** ce gap : toute tentative d'insertion
(ou, plus largement, toute requête) sur `CosmechicsContext.AspNetUsers` contre une base
reconstruite uniquement depuis les migrations échoue avec `Invalid column name
'StreetAddress'` (et les 3 autres). Reproduit lors de l'écriture des tests
d'intégration SQL Server de ce lot ; contourné dans les tests en semant les
utilisateurs via `ApplicationDbContext` (qui ne mappe pas ces colonnes), exactement
comme le fait la vraie inscription Identity en production.

**Correction explicitement hors périmètre** de ce lot (Identity/architecture DbContext,
interdit par le mandat). Code de production concerné, confirmé non affecté par ce
lot : `OrderCheckoutService`/`StripeFulfillmentService`/`StripeWebhookController` ne
lisent ni n'écrivent jamais `CosmechicsContext.AspNetUsers` (le refactor du checkout a
justement éliminé l'unique lecture qui existait dans l'ancien `SummaryPOST`).
Code de production préexistant, non modifié par ce lot, qui reste concerné :
`AspNetUsersController` (CRUD admin), `OrderHeadersController` (listes déroulantes
admin), `AvisController` (affichage), `CartController.Summary()` GET (préremplissage
du formulaire de livraison). **Recommandation** : traiter en priorité au début de
COSMECHIC-IDENTITY-COMMS-001, qui touche déjà Identity.

## 11. Tests

**Total après ce lot : 80 tests, tous PASS** (35 SECURITY-001 + 15 DATA-001 + 30
nouveaux) :

- `CheckoutServiceTests.cs` (8) : panier vide, quantité invalide (0/négative/>50),
  stock insuffisant, produit indisponible, total recalculé côté serveur + snapshot
  produit + aucun décrément de stock + un seul appel Stripe, échec de création de
  session Stripe.
- `StripeFulfillmentServiceTests.cs` (10) : idempotence (chemin rapide), commande déjà
  payée (2e event id distinct), montant incohérent, devise incohérente, stock
  insuffisant au fulfillment (paiement reconnu, statut Pending), paiement échoué,
  commande introuvable, `SessionId` incohérent (protection contre metadata forgée),
  fulfillment réussi (stock décrémenté, statuts, panier vidé, snapshot produit),
  événement sans `OrderId` exploitable.
- `SqlServerFulfillmentConcurrencyTests.cs` (3, **contre SQL Server réel jetable**) :
  concurrence stock=1 (exactement un succès, stock final=0), même `StripeEventId`
  traité deux fois en concurrence (contrainte `UNIQUE`, un seul effet), atomicité
  multi-lignes (aucune mutation partielle).
- `StripeWebhookControllerTests.cs` (9, pipeline HTTP complète) : signature valide +
  paiement réussi, signature invalide, événement dupliqué, type non supporté, commande
  introuvable, montant incohérent, devise incohérente, commande déjà traitée (2e event
  id), paiement échoué.

`SECURITY_001_REGRESSION=NONE`, `DATA_001_REGRESSION=NONE` — les 50 tests de ces deux
lots passent tous, y compris `OrderConfirmationTests.cs`, réécrit pour refléter le
nouveau comportement en lecture seule tout en conservant intégralement les assertions
d'ownership (403/401/404, admin bypass, aucun effet de bord pour une commande
étrangère).

## 12. Risques restants / suites à donner

- Gap Identity (section 10) — à traiter en priorité dans COSMECHIC-IDENTITY-COMMS-001.
- Aucune réservation de stock à expiration n'existe (délibérément, section 21) : un
  client qui laisse une session Stripe ouverte pendant qu'un stock limité s'épuise
  tombera dans le cas documenté en section 8, jamais dans une corruption silencieuse.
- Taxes/frais de port affichés dans `Views/Cart/Summary.cshtml` (TPS/TVQ/livraison) ne
  sont, comme avant ce lot, pas répercutés dans `OrderTotal` ni dans les lignes Stripe
  (bug préexistant, fonctionnel, sans rapport avec la sécurité/l'idempotence — hors
  périmètre explicite de ce lot, non aggravé).
- `IPaymentSessionService`/`StripePaymentSessionService` supprimés (code mort après le
  passage à une vue de confirmation en lecture seule) ; remplacés par
  `IStripeCheckoutService` (création de session) exclusivement.
- EMAIL-001, bug de recherche produit, SEC-007 (upload), vulnérabilités NuGet,
  doublon `StripeSettings` (ARCH-001) : ouverts, non touchés, confirmés hors périmètre.
