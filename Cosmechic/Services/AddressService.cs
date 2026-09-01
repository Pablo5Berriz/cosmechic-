using Cosmechic.Models;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Services
{
    // COSMECHIC-ACCOUNT-001 (section 11/13/14/28) : CRUD adresses, ownership systématique
    // (jamais qu'un contrôleur vérifie seul), invariant "au plus une adresse par défaut"
    // maintenu par une transaction courte + l'index unique filtré du moteur (voir
    // CosmechicsContext) comme dernier rempart contre une course concurrente.
    public class AddressService(CosmechicsContext context) : IAddressService
    {
        private const int MaxDefaultRetryAttempts = 3;

        public async Task<IReadOnlyList<CustomerAddress>> ListForUserAsync(string userId)
        {
            return await context.CustomerAddresses
                .Where(a => a.ApplicationUserId == userId)
                .OrderByDescending(a => a.IsDefaultShipping)
                .ThenByDescending(a => a.UpdatedAt)
                .ToListAsync();
        }

        public async Task<CustomerAddress?> GetOwnedAsync(int addressId, string userId)
        {
            return await context.CustomerAddresses
                .FirstOrDefaultAsync(a => a.Id == addressId && a.ApplicationUserId == userId);
        }

        public async Task<AddressResult> CreateAsync(string userId, AddressInput input, bool setAsDefault)
        {
            if (!TryValidate(input, out var validationError))
            {
                return new AddressRejected(validationError);
            }

            var hasAnyAddress = await context.CustomerAddresses.AnyAsync(a => a.ApplicationUserId == userId);
            // Première adresse d'un utilisateur : toujours par défaut, pour éviter l'état
            // "0 adresse de livraison par défaut alors qu'une adresse existe" au checkout.
            var makeDefault = setAsDefault || !hasAnyAddress;

            if (!makeDefault)
            {
                var address = BuildEntity(userId, input);
                context.CustomerAddresses.Add(address);
                await context.SaveChangesAsync();
                return new AddressSucceeded(address.Id);
            }

            return await SetAsDefaultWithRetryAsync(userId, () =>
            {
                var address = BuildEntity(userId, input);
                context.CustomerAddresses.Add(address);
                return Task.FromResult(address);
            });
        }

        public async Task<AddressResult> UpdateAsync(int addressId, string userId, AddressInput input)
        {
            if (!TryValidate(input, out var validationError))
            {
                return new AddressRejected(validationError);
            }

            var address = await GetOwnedAsync(addressId, userId);
            if (address == null)
            {
                return new AddressRejected("Adresse introuvable.");
            }

            address.Label = input.Label.Trim();
            address.RecipientName = input.RecipientName.Trim();
            address.PhoneNumber = input.PhoneNumber.Trim();
            address.StreetAddress = input.StreetAddress.Trim();
            address.City = input.City.Trim();
            address.State = input.State.Trim();
            address.PostalCode = input.PostalCode.Trim();
            address.CountryCode = input.CountryCode.Trim().ToUpperInvariant();
            address.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();
            return new AddressSucceeded(address.Id);
        }

        public async Task<AddressResult> DeleteAsync(int addressId, string userId)
        {
            var address = await GetOwnedAsync(addressId, userId);
            if (address == null)
            {
                return new AddressRejected("Adresse introuvable.");
            }

            var wasDefault = address.IsDefaultShipping;
            context.CustomerAddresses.Remove(address);
            await context.SaveChangesAsync();

            // Section 13 : si l'adresse supprimée était la valeur par défaut, en désigner
            // automatiquement une autre plutôt que de laisser le compte sans adresse par
            // défaut alors qu'il en reste — jamais 2+, mais 0 alors qu'une existe encore
            // n'est pas non plus l'état voulu pour le checkout.
            if (wasDefault)
            {
                var replacement = await context.CustomerAddresses
                    .Where(a => a.ApplicationUserId == userId)
                    .OrderByDescending(a => a.UpdatedAt)
                    .FirstOrDefaultAsync();
                if (replacement != null)
                {
                    return await SetDefaultAsync(replacement.Id, userId);
                }
            }

            return new AddressSucceeded(addressId);
        }

        public async Task<AddressResult> SetDefaultAsync(int addressId, string userId)
        {
            var existing = await GetOwnedAsync(addressId, userId);
            if (existing == null)
            {
                return new AddressRejected("Adresse introuvable.");
            }

            if (existing.IsDefaultShipping)
            {
                return new AddressSucceeded(existing.Id);
            }

            // Re-résolu à chaque tentative (et non capturé une seule fois) : après un
            // rollback + ChangeTracker.Clear() sur violation d'unicité concurrente,
            // l'entité précédemment chargée est détachée et ne peut plus être sauvegardée
            // telle quelle.
            return await SetAsDefaultWithRetryAsync(userId, async () =>
            {
                var reloaded = await GetOwnedAsync(addressId, userId);
                return reloaded ?? throw new InvalidOperationException("L'adresse a été supprimée entre-temps.");
            });
        }

        // Réutilise le motif "reset transactionnel + retry sur violation d'unicité" déjà
        // établi (RefundOrchestrationService/RestockService, COSMECHIC-COMMERCE-
        // OPERATIONS-001B) : deux "définir par défaut" concurrents pour le même
        // utilisateur ne doivent jamais laisser deux adresses par défaut simultanément.
        private async Task<AddressResult> SetAsDefaultWithRetryAsync(string userId, Func<Task<CustomerAddress>> resolveTarget)
        {
            for (var attempt = 1; attempt <= MaxDefaultRetryAttempts; attempt++)
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                try
                {
                    var target = await resolveTarget();

                    var currentDefaults = await context.CustomerAddresses
                        .Where(a => a.ApplicationUserId == userId && a.IsDefaultShipping && a.Id != target.Id)
                        .ToListAsync();
                    foreach (var current in currentDefaults)
                    {
                        current.IsDefaultShipping = false;
                    }

                    target.IsDefaultShipping = true;
                    await context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new AddressSucceeded(target.Id);
                }
                catch (DbUpdateException ex) when (SqlServerErrors.IsUniqueConstraintViolation(ex))
                {
                    await transaction.RollbackAsync();
                    context.ChangeTracker.Clear();
                    if (attempt == MaxDefaultRetryAttempts)
                    {
                        return new AddressRejected("Une autre modification concurrente a eu lieu, veuillez réessayer.");
                    }
                }
            }

            return new AddressRejected("Une autre modification concurrente a eu lieu, veuillez réessayer.");
        }

        private static CustomerAddress BuildEntity(string userId, AddressInput input) => new()
        {
            ApplicationUserId = userId,
            Label = input.Label.Trim(),
            RecipientName = input.RecipientName.Trim(),
            PhoneNumber = input.PhoneNumber.Trim(),
            StreetAddress = input.StreetAddress.Trim(),
            City = input.City.Trim(),
            State = input.State.Trim(),
            PostalCode = input.PostalCode.Trim(),
            CountryCode = input.CountryCode.Trim().ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        private static bool TryValidate(AddressInput input, out string error)
        {
            if (string.IsNullOrWhiteSpace(input.Label) ||
                string.IsNullOrWhiteSpace(input.RecipientName) ||
                string.IsNullOrWhiteSpace(input.PhoneNumber) ||
                string.IsNullOrWhiteSpace(input.StreetAddress) ||
                string.IsNullOrWhiteSpace(input.City) ||
                string.IsNullOrWhiteSpace(input.State) ||
                string.IsNullOrWhiteSpace(input.PostalCode))
            {
                error = "Tous les champs de l'adresse sont requis.";
                return false;
            }

            var countryCode = input.CountryCode?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(countryCode) || countryCode.Length != 2)
            {
                error = "Le code de pays doit être un code ISO à deux lettres.";
                return false;
            }

            // Section 12 : support fonctionnel Canada uniquement (le calcul de taxe/
            // livraison ne sait honorer aucune autre juridiction actuellement).
            if (countryCode != RegionCodeResolver.CountryCodeCanada)
            {
                error = "Seule la livraison au Canada est actuellement prise en charge.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
