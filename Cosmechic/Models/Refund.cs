using System;

namespace Cosmechic.Models;

// COSMECHIC-COMMERCE-OPERATIONS-001B (section 27/28/29) : enregistrement persistant d'une
// opération de remboursement Stripe. IdempotencyKey est généré et persisté AVANT tout appel
// Stripe (jamais après) — c'est la véritable ancre d'idempotence, contrainte UNIQUE en base,
// réutilisée telle quelle sur une nouvelle tentative (RetryFailedRefundAsync) pour que
// Stripe lui-même renvoie le résultat déjà obtenu plutôt que de créer un second
// remboursement réel si le premier appel avait en réalité réussi côté Stripe malgré une
// erreur perçue côté serveur (timeout, etc. — section 37).
public partial class Refund
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public int? ReturnRequestId { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public string? StripeRefundId { get; set; }

    public decimal Amount { get; set; }

    // COSMECHIC-BUSINESS-POLICY-001 (section 4/5) : décomposition d'Amount, uniquement
    // renseignée par le chemin de remboursement piloté par un retour
    // (RequestReturnRefundAsync) — MerchandiseAmount + ShippingAmount + TaxAmount = Amount
    // (CK_Refunds_Breakdown_Equals_Amount). Le chemin manuel pré-existant (RequestRefundAsync,
    // remboursement admin ad hoc non lié à une politique de retour) laisse les trois à 0 et
    // Cause à null — non classifié, comportement identique à avant ce lot.
    public decimal MerchandiseAmount { get; set; }

    public decimal ShippingAmount { get; set; }

    public decimal TaxAmount { get; set; }

    // Stocké en texte (Enum.ToString()) pour rester cohérent avec la convention déjà en
    // place pour Status/ActorType — jamais une valeur libre : voir RefundCause (modèle fermé).
    public string? Cause { get; set; }

    public string Status { get; set; } = null!;

    public string? Reason { get; set; }

    public string? RequestedByUserId { get; set; }

    public string ActorType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? FailureCode { get; set; }

    public virtual OrderHeader Order { get; set; } = null!;

    public virtual ReturnRequest? ReturnRequest { get; set; }
}
