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
