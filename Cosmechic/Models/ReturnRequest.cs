using System;
using System.Collections.Generic;

namespace Cosmechic.Models;

// COSMECHIC-COMMERCE-OPERATIONS-001B (section 15) : demande de retour, potentiellement
// partielle (ReturnItem porte les lignes/quantités concernées). Une commande peut avoir
// plusieurs ReturnRequest (retours successifs) — c'est pourquoi le statut de retour n'est
// jamais un scalaire agrégé sur OrderHeader, voir docs/audits/COSMECHIC-COMMERCE-OPERATIONS-001B.md.
public partial class ReturnRequest
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    public string ApplicationUserId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? Reason { get; set; }

    public string? CustomerComment { get; set; }

    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 57) : note interne, jamais exposée au
    // client — distincte de CustomerComment, filtrée explicitement de toute vue client.
    public string? AdminComment { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ApprovedAt { get; set; }

    public DateTime? ReceivedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual OrderHeader Order { get; set; } = null!;

    public virtual ICollection<ReturnItem> Items { get; set; } = new List<ReturnItem>();

    public virtual ICollection<Refund> Refunds { get; set; } = new List<Refund>();
}
