namespace Cosmechic.Services
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 4) : modèle fermé, jamais une chaîne libre
    // fournie par le navigateur. Un contrôleur qui accepte ce type ne peut physiquement
    // recevoir qu'une de ces deux valeurs — la décision "qui est en faute" reste entièrement
    // contrôlée côté serveur (l'admin choisit parmi ces deux options via un contrôle fermé,
    // jamais un montant ou un motif de livraison saisi librement).
    public enum RefundCause
    {
        // Le client change simplement d'avis / retourne un produit qui ne lui convient pas :
        // les frais de livraison originaux ne sont jamais remboursés (ShippingRefundAmount=0).
        CustomerRemorse,

        // Erreur du commerçant (mauvais article envoyé, produit défectueux, etc.) : les frais
        // de livraison originaux peuvent être inclus dans le remboursement.
        MerchantFault,
    }
}
