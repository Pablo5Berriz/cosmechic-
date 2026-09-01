using Cosmechic.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cosmechic.Services
{
    // COSMECHIC-BUSINESS-POLICY-001 (section 7) : orchestration centralisée à travers les
    // deux DbContext physiquement séparés (ApplicationDbContext via UserManager pour
    // AspNetUsers, CosmechicsContext pour les données commerciales) — même patron non
    // transactionnel-inter-contexte déjà en place dans DeletePersonalDataModel (ARCH-002).
    // Ordre délibéré : bloquer la connexion D'ABORD (étape 1), puis anonymiser les données
    // commerciales (étape 2) — si l'étape 2 échoue après un succès de l'étape 1, le compte
    // reste au minimum inutilisable plutôt que l'inverse.
    //
    // Ce qui N'EST JAMAIS supprimé ni FK-cassé (section 7/13) : AspNetUsers.Id lui-même
    // (aucun hard-delete), et tous les FK vers cet Id (OrderHeader.ApplicationUserId non
    // nullable, ReturnRequest.ApplicationUserId, Refund.RequestedByUserId,
    // OrderStatusHistory.ActorUserId, StockMovement.ActorUserId) — puisque la ligne
    // AspNetUsers continue d'exister (anonymisée en place), ces FK restent valides par
    // construction, aucune n'est touchée ici.
    public class AccountAnonymizationService(
        UserManager<IdentityUser> userManager,
        CosmechicsContext businessContext,
        ILogger<AccountAnonymizationService> logger) : IAccountAnonymizationService
    {
        public async Task<bool> AnonymizeAsync(string userId)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return false;
            }

            // Étape 1 (Identity) : empêcher toute reconnexion future de façon non
            // réversible — verrouillage permanent ET mot de passe remplacé par une valeur
            // aléatoire que personne ne connaît (défense en profondeur : soit suffit seul à
            // bloquer un login réel, les deux ensemble ferment aussi une éventuelle
            // dérogation de lockout). SecurityStamp change implicitement à chaque appel
            // UserManager ci-dessous, invalidant au passage toute session/cookie active
            // (SecurityStampValidator).
            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

            var externalLogins = await userManager.GetLoginsAsync(user);
            foreach (var login in externalLogins)
            {
                await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
            }

            if (await userManager.HasPasswordAsync(user))
            {
                await userManager.RemovePasswordAsync(user);
            }
            var unusablePassword = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N") + "!Aa1";
            await userManager.AddPasswordAsync(user, unusablePassword);
            await userManager.SetTwoFactorEnabledAsync(user, false);

            // Valeurs non réversibles et non identifiantes, dérivées uniquement de l'Id
            // opaque (jamais du nom/email d'origine) — TLD .invalid réservé (RFC 2606),
            // ne délivre jamais de courriel réel.
            var anonymizedEmail = $"anon-{userId}@anonymized.invalid";
            await userManager.SetPhoneNumberAsync(user, null);
            await userManager.SetEmailAsync(user, anonymizedEmail);
            await userManager.SetUserNameAsync(user, anonymizedEmail);

            // Étape 2 (données commerciales, CosmechicsContext) :
            //
            // - CustomerAddress = carnet d'adresses vivant et éditable, PAS un
            //   enregistrement transactionnel historique (OrderHeader porte déjà son propre
            //   snapshot d'expédition indépendant, section 15/42 de COSMECHIC-ACCOUNT-001) —
            //   supprimé entièrement, aucune obligation de le conserver.
            var addresses = await businessContext.CustomerAddresses
                .Where(a => a.ApplicationUserId == userId)
                .ToListAsync();
            businessContext.CustomerAddresses.RemoveRange(addresses);

            // - OrderHeader = historique transactionnel réel, jamais supprimé. Seuls les
            //   champs directement identifiants du snapshot d'expédition sont anonymisés ;
            //   City/State/PostalCode/CountryCode sont délibérément CONSERVÉS — c'est la
            //   granularité géographique réellement utilisée pour l'audit de la taxe
            //   appliquée (TPS/TVQ par province, COMMERCE-OPERATIONS-001A), une "obligation
            //   comptable" explicitement protégée par la directive (section 7) : la
            //   supprimer casserait la capacité de vérifier après coup qu'une commande
            //   historique a été correctement taxée pour sa juridiction.
            var orders = await businessContext.OrderHeaders
                .Where(o => o.ApplicationUserId == userId)
                .ToListAsync();
            foreach (var order in orders)
            {
                order.Name = "Client anonymisé";
                order.PhoneNumber = "0000000000";
                order.StreetAddress = "[adresse anonymisée]";
            }

            await businessContext.SaveChangesAsync();

            logger.LogInformation(
                "Compte {UserId} anonymisé : connexion bloquée, adresses supprimées, {OrderCount} commande(s) anonymisée(s) (historique conservé).",
                userId, orders.Count);

            return true;
        }
    }
}
