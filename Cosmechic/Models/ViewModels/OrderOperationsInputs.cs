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
    // serveur par RequestReturnRefundAsync. Cause est un type enum fermé (model binding
    // MVC le refuse d'office si la valeur postée ne correspond à aucun des deux membres).
    public class TriggerReturnRefundInput
    {
        public int ReturnRequestId { get; set; }
        public RefundCause Cause { get; set; }
        public string? Reason { get; set; }
    }

    public class ReturnItemIdInput
    {
        public int ReturnItemId { get; set; }
    }
}
