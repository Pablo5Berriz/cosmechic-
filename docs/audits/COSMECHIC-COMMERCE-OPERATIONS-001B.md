# COSMECHIC-COMMERCE-OPERATIONS-001B — Cycle de vie post-achat : annulation, retours, remboursements

- **Lot** : COSMECHIC-COMMERCE-OPERATIONS-001B
- **Base de départ** : `62fbf38` (COSMECHIC-COMMERCE-OPERATIONS-001A, validé)
- **Portée** : cycle de vie de commande (5 dimensions distinctes), annulation avant/après paiement, demandes de retour (partielles), remboursements Stripe (complets/partiels, idempotents, réessayables), remise en stock contrôlée, audit des transitions, reçu durable, surfaces admin/client minimales.
- **Hors scope, volontairement non touché** : tableau de bord client complet (ACCOUNT-001), carnet d'adresses, coupons/promotions avancées, API transporteur, suivi automatisé, expansion fiscale hors juridictions déjà configurées, emails marketing/newsletter, pages légales, conformité facture complète, moteur PDF, SEO, A11Y complète, refonte UX globale, DevOps, monitoring, analytics.

## 0. Recertification technique

```
LOCAL_HEAD=62fbf38f13ec84b01d2f5c2579b371141309771f
WORKTREE=CLEAN (après nettoyage d'artefacts de test wwwroot/Images_Produits, non liés à ce lot)
RESTORE=PASS, BUILD=PASS (0 erreur, Release), TESTS=177/177 PASS
NUGET_VULNERABLE=0 (CRITICAL/HIGH/MODERATE/LOW)
```

## 1. Audit de l'implémentation existante (avant ce lot)

Recherche exhaustive dans `SD.cs` et tous les sites de mutation d'`OrderHeader`/`Produit.Stock` :

```
CURRENT_ORDER_STATUSES=Pending/Processing/Cancelled (réellement utilisés) + Approved/Shipped/Refunded (déclarés dans SD.cs, JAMAIS lus ni écrits nulle part — code mort, supprimés ce lot)
CURRENT_PAYMENT_STATUSES=Pending/Approved/Rejected (réellement utilisés) + ApprovedForDelayedPayment (déclaré, mort — supprimé)
CURRENT_REFUND_IMPLEMENTATION=NONE (aucun appel Stripe Refund, aucun modèle Refund)
CURRENT_RETURN_IMPLEMENTATION=NONE (aucun modèle ReturnRequest, aucune route de retour)
CURRENT_CANCELLATION_IMPLEMENTATION=NONE (le seul "Cancelled" existant était un effet de bord automatique de StripeFulfillmentService sur paiement échoué — jamais une action client/admin)
CURRENT_RESTOCK_IMPLEMENTATION=NONE (seule mutation de stock existante : décrément lors du fulfillment ; ProduitsController.Edit fixe une valeur absolue, pas un delta contrôlé)
```

**Découverte critique de l'audit** (au-delà de la checklist du mandat) : `OrderHeadersController.Edit` (scaffold admin) liait librement `OrderStatus`/`PaymentStatus`/`OrderTotal`/`SessionId`/`PaymentIntentId`/`ApplicationUserId` via un formulaire brut, contournant toute logique de transition et violant l'immuabilité du snapshot financier (COMMERCE-001A, section 80). De plus, la mutation utilisait `_context.Update(entité_liée)` sur une entité ne portant que les champs bindés — un narrowing naïf du `[Bind]` aurait donc **écrasé silencieusement** OrderStatus/OrderTotal/etc. avec leurs valeurs CLR par défaut (null/0) à chaque édition admin. Corrigé (§9) en chargeant l'entité existante et en n'y reportant que les champs explicitement autorisés.

## 2. Modèle d'état cible — cinq dimensions, jamais mélangées

```
OrderStatus:        Pending → Confirmed → Cancelled | Completed
PaymentStatus:       Pending → Paid → PartiallyRefunded → Refunded
                              → Failed
FulfillmentStatus:  Unfulfilled → Processing → Shipped → Delivered
                                → Cancelled
ReturnStatus:        Requested → Approved → Received → Completed
                                → Rejected
RefundStatus:        Pending → Succeeded
                             → Failed
```

**Décision de conception explicite** (structure jugée meilleure qu'un cinquième scalaire sur `OrderHeader`) : `ReturnStatus` et `RefundStatus` ne sont **jamais** des colonnes agrégées sur `OrderHeader`. Une commande peut avoir 0..N `ReturnRequest` et 0..N `Refund`, chacun avec son propre statut — un scalaire agrégé serait soit incorrect (quel retour représente-t-il si plusieurs sont en cours ?), soit une seconde source de vérité à synchroniser manuellement (section 79 du mandat interdit explicitement cette duplication). `ReturnStatus`/`RefundStatus` sont donc portés par les enregistrements `ReturnRequest.Status`/`Refund.Status` eux-mêmes, consultables via les collections de navigation d'`OrderHeader`. `PaymentStatus`, en revanche, reste un scalaire légitime car il caractérise l'état du paiement AU NIVEAU DE LA COMMANDE (dérivé de la somme des `Refund` réussis par `IOrderLifecycleService`/`RefundOrchestrationService`, jamais fixé arbitrairement ailleurs).

### Migration des données historiques (aucune preuve fabriquée)

```sql
-- FulfillmentStatus déduit de l'ANCIEN OrderStatus (avant remappage) :
'Processing' -> 'Processing' ; 'Cancelled' -> 'Cancelled' ; sinon -> 'Unfulfilled'
-- OrderStatus : 'Processing' -> 'Confirmed' ; Pending/Cancelled inchangés
-- PaymentStatus : 'Approved' -> 'Paid' ; 'Rejected' -> 'Failed' ; Pending inchangé
```

Aucune commande historique n'est marquée `Shipped`/`Delivered`/`Refunded` sans preuve : le seul mapping possible depuis l'ancien modèle ne permet jamais de déduire une expédition réelle. Validé contre un vrai SQL Server jetable avec 3 commandes historiques simulées (Pending/Pending, Processing/Approved, Cancelled/Rejected) → remappées respectivement en (Pending/Pending/Unfulfilled), (Confirmed/Paid/Processing), (Cancelled/Failed/Cancelled), `OrderTotal` et `RefundedAmount=0` inchangés.

## 3. `IOrderLifecycleService` — autorité centrale des transitions

Seul point d'écriture pour `OrderStatus`/`PaymentStatus`/`FulfillmentStatus`. Table de transitions explicite par dimension (`Dictionary<string, HashSet<string>>`), rejet avec raison explicite pour toute transition non listée, no-op idempotent pour une transition vers le même état. Chaque transition appliquée écrit une ligne `OrderStatusHistory` (qui/quoi/quand/pourquoi — `EventType`/`PreviousStatus`/`NewStatus`/`Reason`/`ActorUserId`/`ActorType`/`CreatedAt`). Volontairement sans `SaveChangesAsync` propre : chaque appelant (déjà transactionnel — `StripeFulfillmentService`, `RefundOrchestrationService`, `CancellationService`) persiste dans SA propre transaction, composant avec les garanties de concurrence existantes plutôt que de les dupliquer.

`OrderCheckoutService`/`StripeFulfillmentService` (ECOM-CORE-001/COMMERCE-001A) ont été réécrits pour ne plus jamais assigner `order.OrderStatus = "..."` directement — chaque affectation passe par `TryTransition*`. Aucun changement de comportement métier : les mêmes conditions produisent les mêmes issues (`Pending`→`Confirmed`+`Processing` sur fulfillment réussi, `Pending`→`Cancelled` sur paiement échoué, etc.), simplement validées et auditées.

## 4. Annulation (`ICancellationService`)

Politique technique minimale (section 12 du mandat — aucune règle commerciale arbitraire type délai de rétractation) : bloque uniquement déjà expédiée (`FulfillmentStatus` Shipped/Delivered), déjà annulée, déjà entièrement remboursée. Une commande `Pending` (jamais payée, stock jamais décrémenté) est annulée sans aucune remise en stock (rien à remettre). Une commande payée déclenche automatiquement un remboursement du solde remboursable complet via le même workflow idempotent que tout remboursement manuel — jamais un simple changement de statut ignorant la réalité financière (section 14).

## 5. Retours (`IReturnService`)

`ReturnRequest`/`ReturnItem` (potentiellement partiels). `CanRequestReturnAsync` centralisée (réutilisée par la création ET exposable à une vue) : porte technique minimale = commande expédiée/livrée avec paiement confirmé (jamais de fenêtre de jours inventée — `RETURN_WINDOW_DAYS=TODO_REQUIRES_BUSINESS_CONFIGURATION`, non implémentée faute de règle métier existante). Invariant quantité : `Σ(ReturnItem.Quantity non rejetés pour cet OrderDetail) + nouvelle demande <= OrderDetail.Count`. Ownership vérifié à deux niveaux (commande ET ligne appartiennent bien au demandeur). Machine d'état dédiée (`Requested → Approved/Rejected`, `Approved → Received`, `Received → Completed`).

## 6. Remboursements Stripe — le cœur technique du lot

**Un remboursement n'est jamais une simple mise à jour de statut.** Frontière DB/Stripe explicite (section 35/36, aucune transaction distribuée tentée) :

1. **Réservation** : dans une transaction locale, `OrderHeader.RefundedAmount += montant` et insertion d'un `Refund{Status=Pending, IdempotencyKey=nouveau GUID}`, protégée par retry optimiste sur `OrderHeader.RowVersion` (même patron que le décrément de stock de `StripeFulfillmentService`, ECOM-CORE-001) — c'est CETTE étape, pas une simple lecture "si soldé alors insérer", qui rend deux demandes concurrentes réellement exclusives. `CK_OrderHeaders_RefundedAmount_WithinTotal` (`RefundedAmount >= 0 AND RefundedAmount <= OrderTotal`) est le filet de sécurité moteur.
2. **Appel Stripe**, une fois la réservation committée, avec `RequestOptions.IdempotencyKey = Refund.IdempotencyKey` (généré et persisté AVANT l'appel, jamais après) — Stripe garantit qu'une nouvelle tentative avec la même clé renvoie le résultat déjà obtenu plutôt que de créer un second remboursement réel.
3. **Finalisation** (Succeeded/Failed) dans une écriture séparée, idempotente (no-op si déjà finalisé), déclenchée soit par le retour synchrone de l'appel Stripe, soit par le webhook `refund.updated` (convergence — section 37) — l'un ou l'autre, selon celui qui arrive en premier.
4. **Échec Stripe** → libère la réservation (`RefundedAmount -= montant`) et marque `Refund.Status=Failed` : une nouvelle tentative reste possible.
5. **`RetryFailedRefundAsync`** réutilise la MÊME `IdempotencyKey` : si le premier appel avait en réalité réussi côté Stripe malgré une erreur perçue côté serveur (timeout), la relance renvoie ce résultat déjà obtenu au lieu d'un second remboursement réel.

`IStripeRefundService` (seam serveur uniquement, jamais appelé directement par un controller) enveloppe `Stripe.RefundService.Create`. `Metadata["RefundRecordId"]` posé sur le `RefundCreateOptions` permet au webhook de retrouver notre ligne même si l'écriture post-appel a échoué (repli sur `StripeRefundId` sinon).

```
REFUND_SHIPPING_POLICY=TODO_REQUIRES_BUSINESS_CONFIGURATION
REFUND_TAX_POLICY=TODO_REQUIRES_BUSINESS_CONFIGURATION
BUSINESS_CONFIGURATION_REQUIRED=OUI — aucune règle n'existe sur la part de livraison/taxe remboursée lors d'un retour partiel ; le solde remboursable actuel est calculé sur OrderTotal complet (section 25/26), sans logique fiscale fabriquée.
```

## 7. Webhook `refund.updated`

Ajouté à `StripeWebhookController` (même route `/webhooks/stripe`, même vérification de signature HMAC obligatoire — jamais un second endpoint moins sécurisé). Seul `refund.updated` est supporté (pas `charge.refunded`) : il porte directement l'objet `Refund` (Id/Status/Metadata), permettant une réconciliation précise par `Refund.Id`, contrairement à `charge.refunded` qui agrège au niveau de la charge. Même barrière d'idempotence à deux niveaux que les événements checkout (`ProcessedStripeEvents` — AnyAsync + catch sur violation de contrainte UNIQUE), extraite dans `SqlServerErrors` partagé avec `StripeFulfillmentService`.

## 8. Remise en stock (`IRestockService`) + `StockMovement`

Remboursement financier ≠ retour physique (section 38) : jamais automatique. `CompleteRestockAsync` exige `ReturnRequest.Status` au moins `Received`, incrémente `Produit.Stock`, marque `ReturnItem.Restocked=true` (idempotence — section 39) dans une boucle de retry optimiste sur `Produit.RowVersion` (même patron que le décrément de fulfillment) : deux complétions concurrentes de la MÊME `ReturnItem` ne peuvent produire qu'un seul incrément (vérifié contre SQL Server réel).

`StockMovement` introduite ce lot (mandat section 41, reconsidérée explicitement à la demande du PM) plutôt que reportée à ADMIN-001 : puisque le retour ajoute une mutation positive à côté de la seule mutation négative existante, la traçabilité complète était nécessaire dès maintenant. Le décrément de fulfillment existant (`StripeFulfillmentService`) écrit désormais aussi une ligne `StockMovement` — extension mineure d'un site d'écriture existant, comportement de mutation du stock lui-même inchangé.

## 9. `OrderHeadersController.Edit`/`Create` — correction de l'overposting

`[Bind]` réduit à `Id,Name,PhoneNumber,StreetAddress,City,State,PostalCode` (adresse/nom uniquement — plus jamais OrderStatus/PaymentStatus/OrderTotal/SessionId/PaymentIntentId/ApplicationUserId). `Edit` POST charge désormais l'entité existante et ne reporte que ces 6 champs (voir §1 pour le bug d'écrasement silencieux que l'ancien patron `_context.Update(entité_narrowée)` aurait introduit). `Create` devient de facto non fonctionnel pour une commande complète (`ApplicationUserId` non-nullable non bindé → `ModelState.IsValid` faux) — cohérent avec le commentaire préexistant : aucune preuve qu'un admin doive fabriquer une commande arbitraire hors du flux de checkout réel.

## 10. Suivi d'expédition et surfaces admin/client

`OrderHeader.ShippedAt`/`DeliveredAt` ajoutés ; `TrackingNumber`/`Carrier` (déjà existants, jamais renseignés avant ce lot) désormais alimentés par `OrderOperationsController.MarkShipped`. Aucune entité `Shipment` : une commande = un colis actuellement, un modèle plus riche serait une sur-architecture non justifiée (section 43).

**Admin** (`OrderOperationsController`, `[Authorize(Roles="Admin")]`) : vue de détail unique (statuts, snapshot financier, historique, retours, remboursements) + actions POST+antiforgery dédiées (marquer expédiée/livrée, annuler, approuver/rejeter/recevoir/compléter un retour, déclencher/retenter un remboursement, compléter une remise en stock) — chacune via un DTO ciblé, jamais une entité complète.

**Client** (`ReturnsController` + ajouts `CartController`/`OrderHeadersController`) : demander un retour (avec quantité maximale retournable calculée serveur), consulter le statut d'un retour, reçu imprimable (`Cart/Receipt`), annuler sa propre commande. Rien de plus (pas de tableau de bord complet).

## 11. Reçu et identifiant public de commande

`Cart/Receipt` — HTML imprimable durable (items/SKU/prix/quantité/sous-total/livraison/taxes/rabais/total/statut paiement), même contrôle d'ownership que `OrderConfirmation`. Explicitement documenté comme un reçu, pas une facture fiscale conforme (`TODO_REQUIRES_BUSINESS_CONFIGURATION` si des informations fiscales du vendeur sont un jour nécessaires). Aucun moteur PDF introduit (HTML imprimable suffisant, section 48).

**Identifiant public de commande** : évalué et **non introduit**. L'ID séquentiel `OrderHeader.Id` reste l'identifiant affiché. Justification : l'ownership est déjà systématiquement vérifié à chaque accès (jamais une sécurité par obscurité, section 50), aucune exigence métier pour un format `COS-2026-000123` n'a été formulée, et l'ajouter maintenant serait une colonne/migration/affichage sans bénéfice fonctionnel démontré — reporté sans être oublié (documenté ici pour un futur lot si une exigence apparaît).

## 12. Tests

```
TESTS_BEFORE=177
TESTS_AFTER=239
NEW_TESTS=62
TESTS_FAIL=0
SQL_SERVER_TESTS=29/29 PASS (dont 3 nouveaux : concurrence remboursement, concurrence restock, cohérence CHECK)
```

| Fichier | Portée |
|---|---|
| `OrderLifecycleServiceTests.cs` | Transitions valides/invalides des 3 dimensions, no-op idempotent, écriture d'historique. |
| `CancellationServiceTests.cs` | Pending non payée (sans refund), payée (refund déclenché), expédiée (refusé), déjà annulée/remboursée (refusé), commande étrangère (refusé). |
| `ReturnServiceTests.cs` | Commande étrangère refusée, retour éligible accepté, retour partiel, quantité 0/excessive refusée, double réclamation excessive refusée, commande non expédiée refusée, cycle complet Requested→Completed. |
| `RefundOrchestrationServiceTests.cs` | Remboursement complet/partiel, dépassement de solde refusé (immédiat et cumulé), déjà entièrement remboursé refusé, échec Stripe libère la réservation, retry réutilise la même IdempotencyKey, retry d'un remboursement déjà réussi refusé, aucun PaymentIntent refusé, **snapshot financier jamais muté par un remboursement** (régression COMMERCE-001A explicite). |
| `RestockServiceTests.cs` | Remise en stock réussie + ligne StockMovement, double appel idempotent (stock incrémenté une fois), retour non reçu refusé. |
| `SqlServerRefundAndRestockConcurrencyTests.cs` (SQL Server réel) | 100 $ total, 80 $+50 $ concurrents → jamais les deux ne réussissent (≤ 100 $ toujours) ; même ReturnItem, deux complétions concurrentes → stock incrémenté exactement une fois ; contrainte CHECK accepte un solde exact. |
| `StripeRefundWebhookTests.cs` | Événement signé valide finalise un Refund resté Pending, signature invalide (400, aucune mutation), doublon (effet unique), remboursement inconnu (200, pas d'exception), événement tardif sur remboursement déjà finalisé (no-op), échec Stripe libère la réservation. |
| `PostPurchaseAuthorizationTests.cs` | Reçu (owner/étranger/admin/anonyme), un client ne peut ni accéder à `OrderOperations`, ni marquer expédiée/livrée, ni déclencher un remboursement, ni approuver un retour, ni compléter une remise en stock, ni annuler la commande d'un autre. |
| Fichiers ECOM-CORE-001/COMMERCE-001A existants (adaptés) | Renommage des constantes SD (`StatusPending`→`OrderStatusPending` etc.), injection d'`IOrderLifecycleService` — comportement fonctionnel inchangé, toujours verts. |

## 13. Gates finaux

```
BUILD=PASS (0 erreur, Release)
WARNINGS_BEFORE=102 (--no-incremental, baseline 62fbf38 : CS8618:50, CS8602:18, CS8600:12, CS8604:6, CS8625:4, CS8601:4, ASP0019:4, CS8619:2, CS1998:2)
WARNINGS_AFTER=102 (mêmes comptes exacts par code)
NEW_CODE_WARNINGS=0 (un CS0108 introduit puis corrigé — ReturnsController.Request renommée pour ne plus masquer ControllerBase.Request)
NUGET_CRITICAL=0, NUGET_HIGH=0, NUGET_MODERATE=0, NUGET_LOW=0
TESTS=239/239 PASS
MIGRATION_MODEL_DRIFT=NONE (ApplicationDbContext et CosmechicsContext)
DATABASE_RECONSTRUCTIBLE=YES (validé sur SQL Server 2022 jetable, empty DB → migrations Identity → migrations CosmechicsContext, y compris le remappage de données historiques)
SECRET_SCAN=CLEAN
```
