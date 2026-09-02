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
    //
    // COSMECHIC-LEGAL-POLICY-IMPLEMENTATION-001 (section 10/11) : le lot de recherche a
    // confirmé deux fuites de confidentialité réelles, corrigées ici :
    //   1. Les champs StreetAddress/City/State/PostalCode de Cosmechic.Models.AspNetUser
    //      (CosmechicsContext, propriétés CLR réelles consommées par AspNetUsersController —
    //      PAS des propriétés fantômes) survivaient intégralement à une anonymisation de
    //      compte. Ils sont maintenant effacés au même titre que les champs d'OrderHeader.
    //   2. Les lignes ShoppingCart liées au compte n'étaient jamais nettoyées, laissant des
    //      paniers abandonnés/orphelins survivre indéfiniment à l'anonymisation. Un panier
    //      n'est pas un enregistrement comptable (contrairement à OrderHeader) — supprimé
    //      entièrement, comme CustomerAddress.
    // Aucun second modèle concurrent créé : on continue d'utiliser exclusivement
    // Cosmechic.Models.AspNetUser (CosmechicsContext), déjà la source de vérité active pour
    // ces colonnes.
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

            // - Cosmechic.Models.AspNetUser (champs legacy StreetAddress/City/State/
            //   PostalCode, écran admin AspNetUsersController) : mêmes raisons que
            //   CustomerAddress — ce ne sont pas des données transactionnelles historiques,
            //   rien n'oblige à les conserver une fois le compte anonymisé.
            var legacyProfile = await businessContext.AspNetUsers.FirstOrDefaultAsync(u => u.Id == userId);
            if (legacyProfile != null)
            {
                legacyProfile.StreetAddress = null;
                legacyProfile.City = null;
                legacyProfile.State = null;
                legacyProfile.PostalCode = null;
            }

            // - ShoppingCart : donnée vivante non transactionnelle (contrairement à
            //   OrderHeader/OrderDetail, jamais touchés ici). Un panier abandonné lié à ce
            //   compte ne doit pas survivre indéfiniment à son anonymisation — supprimé
            //   entièrement, jamais désassocié (ApplicationUserId n'a aucune utilité sans
            //   propriétaire réel).
            var cartItems = await businessContext.ShoppingCarts
                .Where(c => c.ApplicationUserId == userId)
                .ToListAsync();
            businessContext.ShoppingCarts.RemoveRange(cartItems);

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
                "Compte {UserId} anonymisé : connexion bloquée, {AddressCount} adresse(s) supprimée(s), profil legacy effacé, {CartCount} ligne(s) de panier supprimée(s), {OrderCount} commande(s) anonymisée(s) (historique conservé).",
                userId, addresses.Count, cartItems.Count, orders.Count);

            return true;
        }
    }
}
