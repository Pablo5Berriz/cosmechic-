using System;

namespace Cosmechic.Models;

// Préparation COSMECHIC-DATA-001 pour l'idempotence des webhooks Stripe de
// COSMECHIC-ECOM-CORE-001 (section 13 du mandat). Modèle minimal : un identifiant
// d'événement Stripe unique, un statut de traitement, et une référence de commande
// facultative. Aucune logique de traitement de webhook n'est développée dans ce lot —
// seule la table est créée pour que sa migration soit intégrée proprement dès
// maintenant. Ne contient aucune donnée de carte bancaire.
public partial class ProcessedStripeEvent
{
    public int Id { get; set; }

    public string StripeEventId { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public DateTime ReceivedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string ProcessingStatus { get; set; } = null!;

    public int? OrderId { get; set; }

    public virtual OrderHeader? Order { get; set; }
}
