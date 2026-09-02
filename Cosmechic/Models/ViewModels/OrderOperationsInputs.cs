using Cosmechic.Services;

namespace Cosmechic.Models.ViewModels
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B (section 54) : DTO ciblés pour chaque action
    // post-achat — jamais une entité métier complète (OrderHeader, Refund, ReturnRequest...)
    // liée depuis le navigateur.
    public class MarkShippedInput
    {
        public int OrderId { get; set; }
        public string? TrackingNumber { get; set; }
        public string? Carrier { get; set; }
    }

    public class OrderIdReasonInput
    {
        public int OrderId { get; set; }
        public string? Reason { get; set; }
    }

    public class ReturnRequestIdCommentInput
    {
        public int ReturnRequestId { get; set; }
        public string? Comment { get; set; }
    }

    public class TriggerRefundInput
    {
        public int OrderId { get; set; }
        public decimal Amount { get; set; }
        public int? ReturnRequestId { get; set; }
        public string? Reason { get; set; }
    }

    public class RefundIdInput
    {
        public int RefundId { get; set; }
    }

    // COSMECHIC-BUSINESS-POLICY-001 (section 4) : contrairement à TriggerRefundInput, ne
    // porte volontairement AUCUN champ Amount — le montant est entièrement calculé côté
    // serveur par RequestReturnRefundAsync.
    // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 8) : Cause a été retiré de ce DTO —
    // il n'est plus fourni par l'admin/le navigateur du tout, il est dérivé côté serveur à
    // partir des ReturnReasonCategory déjà persistées sur les lignes du retour.
    public class TriggerReturnRefundInput
    {
        public int ReturnRequestId { get; set; }
        public string? Reason { get; set; }
    }

    public class ReturnItemIdInput
    {
        public int ReturnItemId { get; set; }
    }
}
