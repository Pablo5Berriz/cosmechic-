namespace Cosmechic.Models.ViewModels
{
    // COSMECHIC-ACCOUNT-001 : DTOs étroits de l'espace compte. Aucun ne porte de champ
    // sensible/administratif (Id d'un tiers, rôle, statut de commande/paiement, montant
    // de remboursement...) — section 29.

    public class ProfileVM
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailConfirmed { get; set; }
        public string? PhoneNumber { get; set; }
    }

    public class ProfileInput
    {
        public string? PhoneNumber { get; set; }
    }

    public class AddressFormInput
    {
        public int? Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string RecipientName { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string StreetAddress { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string CountryCode { get; set; } = "CA";
        public bool SetAsDefault { get; set; }
    }

    public class AddressIdInput
    {
        public int Id { get; set; }
    }

    public class AccountDashboardVM
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailConfirmed { get; set; }
        public IReadOnlyList<OrderHeader> RecentOrders { get; set; } = Array.Empty<OrderHeader>();
        public int OrdersInProgressCount { get; set; }
        public CustomerAddress? DefaultAddress { get; set; }
        public int AddressCount { get; set; }
    }

    public class PagedOrdersVM
    {
        public IReadOnlyList<OrderHeader> Orders { get; set; } = Array.Empty<OrderHeader>();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    }
}
