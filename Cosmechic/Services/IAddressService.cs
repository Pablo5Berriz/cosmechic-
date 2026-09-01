using Cosmechic.Models;

namespace Cosmechic.Services
{
    public abstract record AddressResult;
    public sealed record AddressSucceeded(int AddressId) : AddressResult;
    public sealed record AddressRejected(string Reason) : AddressResult;

    public sealed record AddressInput(
        string Label,
        string RecipientName,
        string PhoneNumber,
        string StreetAddress,
        string City,
        string State,
        string PostalCode,
        string CountryCode);

    // COSMECHIC-ACCOUNT-001 (section 11/13/14) : seule source de vérité pour le CRUD
    // d'adresses client — ownership et invariant "0 ou 1 adresse par défaut" appliqués
    // ici, jamais dans un controller ou une vue.
    public interface IAddressService
    {
        Task<IReadOnlyList<CustomerAddress>> ListForUserAsync(string userId);

        Task<CustomerAddress?> GetOwnedAsync(int addressId, string userId);

        Task<AddressResult> CreateAsync(string userId, AddressInput input, bool setAsDefault);

        Task<AddressResult> UpdateAsync(int addressId, string userId, AddressInput input);

        Task<AddressResult> DeleteAsync(int addressId, string userId);

        Task<AddressResult> SetDefaultAsync(int addressId, string userId);
    }
}
