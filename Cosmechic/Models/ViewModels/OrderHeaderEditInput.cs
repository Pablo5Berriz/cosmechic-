namespace Cosmechic.Models.ViewModels
{
    // COSMECHIC-COMMERCE-OPERATIONS-001B-CLOSURE-1 : DTO étroit dédié à
    // OrderHeadersController.Edit, plutôt qu'un [Bind] sur l'entité OrderHeader complète.
    //
    // Un [Bind] narrowé sur OrderHeader laisse malgré tout ModelState évaluer TOUTES les
    // propriétés non-nullables de l'entité (Nullable activé côté projet ⇒ validation
    // "Required" implicite sur chaque référence non-nullable, y compris celles hors du
    // [Bind]). ApplicationUserId n'étant jamais posté, ModelState.IsValid devenait
    // systématiquement false — cassant l'action pour tout usage légitime, pas seulement
    // pour une tentative d'overposting. Un DTO qui ne porte que les champs réellement
    // éditables élimine ce problème à la racine.
    public class OrderHeaderEditInput
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string StreetAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
    }
}
