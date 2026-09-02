namespace Cosmechic.Utility
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 2/6/7) : cinq dimensions distinctes et
    // jamais mélangées. Avant ce lot, une seule chaîne "OrderStatus" mélangeait commande,
    // paiement et expédition (ex. "Processing" signifiait à la fois "payé" ET "en cours de
    // préparation" ; "Cancelled" servait aussi bien à un échec de paiement qu'à une future
    // annulation). StatusApproved/StatusShipped/StatusRefunded/PaymentStatusDelayedPayment
    // étaient déclarés mais jamais lus ni écrits nulle part (vérifié par recherche
    // exhaustive avant ce lot) — supprimés plutôt que conservés comme code mort.
    public static class SD
    {
        public const string Role_Customer = "Customer";
        public const string Role_Company = "Company";
        public const string Role_Admin = "Admin";
        public const string Role_Employee = "Employee";

        // Cycle de vie de la commande elle-même (jamais l'état du paiement ou de
        // l'expédition). Seul IOrderLifecycleService peut faire transiter ces valeurs.
        public const string OrderStatusPending = "Pending";
        public const string OrderStatusConfirmed = "Confirmed";
        public const string OrderStatusCancelled = "Cancelled";
        public const string OrderStatusCompleted = "Completed";

        // État du paiement Stripe pour cette commande (jamais l'état de la commande ou de
        // l'expédition). PartiallyRefunded/Refunded sont dérivés de la somme des Refund
        // réussis (RefundOrchestrationService), jamais fixés arbitrairement ailleurs.
        public const string PaymentStatusPending = "Pending";
        public const string PaymentStatusPaid = "Paid";
        public const string PaymentStatusPartiallyRefunded = "PartiallyRefunded";
        public const string PaymentStatusRefunded = "Refunded";
        public const string PaymentStatusFailed = "Failed";

        // État de la préparation/expédition physique (jamais l'état du paiement).
        public const string FulfillmentStatusUnfulfilled = "Unfulfilled";
        public const string FulfillmentStatusProcessing = "Processing";
        public const string FulfillmentStatusShipped = "Shipped";
        public const string FulfillmentStatusDelivered = "Delivered";
        public const string FulfillmentStatusCancelled = "Cancelled";

        // État d'une ReturnRequest individuelle (une commande peut en avoir plusieurs :
        // volontairement PAS un scalaire agrégé sur OrderHeader, voir
        // docs/audits/COSMECHIC-COMMERCE-OPERATIONS-001B.md section "modèle d'état").
        public const string ReturnStatusRequested = "Requested";
        // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 7) : voie de triage distincte
        // pour toute demande portant au moins une ligne SafetyOrAdverseReaction — jamais
        // traitée comme une demande de retour ordinaire tant qu'un admin ne l'a pas
        // explicitement libérée (ReleaseSafetyReviewAsync) vers ReturnStatusRequested.
        public const string ReturnStatusNeedsSafetyReview = "NeedsSafetyReview";
        public const string ReturnStatusApproved = "Approved";
        public const string ReturnStatusRejected = "Rejected";
        public const string ReturnStatusReceived = "Received";
        public const string ReturnStatusCompleted = "Completed";

        // État d'un Refund individuel (une commande peut en avoir plusieurs : volontairement
        // PAS un scalaire agrégé sur OrderHeader non plus).
        public const string RefundStatusPending = "Pending";
        public const string RefundStatusSucceeded = "Succeeded";
        public const string RefundStatusFailed = "Failed";

        // Acteur d'une transition (OrderStatusHistory.ActorType, section 11) : jamais une
        // chaîne libre envoyée par le navigateur — dérivé côté serveur.
        public const string ActorTypeCustomer = "Customer";
        public const string ActorTypeAdmin = "Admin";
        public const string ActorTypeSystem = "System";
        public const string ActorTypeStripeWebhook = "StripeWebhook";

        // Motif d'une ligne StockMovement (section 41).
        public const string StockMovementReasonFulfillment = "Fulfillment";
        public const string StockMovementReasonReturnRestock = "ReturnRestock";

        public const string SessionCart = "SessionShoppingCart";
    }
}
