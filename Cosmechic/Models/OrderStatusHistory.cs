using System;

namespace Cosmechic.Models;

// COSMECHIC-COMMERCE-OPERATIONS-001B (section 10/11) : piste d'audit persistante — répond
// toujours à qui/quoi/quand/pourquoi. Une ligne par changement d'UNE dimension (OrderStatus
// OU PaymentStatus OU FulfillmentStatus OU un événement retour/remboursement/restock) :
// EventType nomme la dimension/l'événement, PreviousStatus/NewStatus portent les valeurs de
// cette seule dimension. Choisi plutôt qu'une table large à colonnes multiples : plus
// simple, extensible sans migration à chaque nouvel événement (ex. "ReturnRequested" n'a
// pas de "statut précédent" au sens propre, seulement NewStatus).
public partial class OrderStatusHistory
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string EventType { get; set; } = null!;

    public string? PreviousStatus { get; set; }

    public string? NewStatus { get; set; }

    public string? Reason { get; set; }

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 11) : jamais une chaîne libre envoyée par
    // le navigateur — toujours dérivé côté serveur de l'identité authentifiée ou du contexte
    // d'exécution (webhook Stripe, traitement système).
    public string? ActorUserId { get; set; }

    public string ActorType { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual OrderHeader Order { get; set; } = null!;
}
