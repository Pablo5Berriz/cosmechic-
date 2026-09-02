namespace Cosmechic.Services
{
    // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 5) : type fermé, jamais une chaîne
    // libre. Détermine à lui seul l'éligibilité (fenêtre commerciale, déclarations d'état du
    // produit) et le routage (voie normale vs voie de triage sécurité) d'une ligne de retour —
    // le texte libre (ReturnItem.Reason / ReturnRequest.CustomerComment) reste un complément
    // narratif, jamais une source de vérité pour ces décisions.
    //
    // LegacyUnclassified : valeur réservée au backfill de migration (section 14) pour les
    // ReturnItem créés avant l'introduction de ce champ — jamais choisissable par un client ni
    // un admin, jamais utilisée pour classer une nouvelle demande. Voir la migration
    // AddReturnReasonCategory et ReturnPolicyImplementationTests pour la preuve qu'aucune ligne
    // historique n'est requalifiée arbitrairement en ChangeOfMind.
    public enum ReturnReasonCategory
    {
        ChangeOfMind,
        DefectOrNonConformity,
        WrongItemOrMerchantFault,
        SafetyOrAdverseReaction,
        LegacyUnclassified,
    }
}
