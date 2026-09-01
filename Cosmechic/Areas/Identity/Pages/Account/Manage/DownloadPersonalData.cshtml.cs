// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmechic.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cosmechic.Areas.Identity.Pages.Account.Manage
{
    public class DownloadPersonalDataModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly CosmechicsContext _businessContext;
        private readonly ILogger<DownloadPersonalDataModel> _logger;

        public DownloadPersonalDataModel(
            UserManager<IdentityUser> userManager,
            CosmechicsContext businessContext,
            ILogger<DownloadPersonalDataModel> logger)
        {
            _userManager = userManager;
            _businessContext = businessContext;
            _logger = logger;
        }

        public IActionResult OnGet()
        {
            return NotFound();
        }

        // COSMECHIC-BUSINESS-POLICY-001 (section 8) : étend l'export scaffoldé par défaut
        // (Identity seul) aux données commerciales appartenant réellement au client.
        // IDOR (section 8) : chaque requête ci-dessous filtre explicitement sur
        // `user.Id` (l'utilisateur AUTHENTIFIÉ courant, jamais un identifiant fourni par le
        // client) — aucune donnée d'un autre utilisateur ne peut être retournée par
        // construction. Voir PersonalDataExportTests pour la preuve.
        //
        // Exclusions obligatoires (section 8/9), jamais incluses :
        //   - Refund.StripeRefundId, Refund.IdempotencyKey, Refund.FailureCode (internes
        //     Stripe/techniques, pas des données personnelles du client) ;
        //   - ReturnRequest.AdminComment (note interne, jamais exposée au client — déjà la
        //     règle établie en COMMERCE-OPERATIONS-001B) ;
        //   - OrderHeader.PaymentIntentId (identifiant technique Stripe interne) ;
        //   - RowVersion et tout jeton de concurrence ;
        //   - aucun secret (mot de passe, clé API) n'a jamais été inclus ici.
        //
        // Avis/reviews (TemoignagesClient) : DÉLIBÉRÉMENT EXCLUS de cet export. Ce modèle
        // ne porte aucune clé étrangère vers AspNetUsers (seulement un instantané texte
        // libre `Nom`) — il n'existe donc aucun moyen fiable de déterminer "quels avis
        // appartiennent à ce client" sans une heuristique de correspondance par nom, qui
        // risquerait d'inclure l'avis d'un autre utilisateur portant le même nom affiché
        // (une fuite de type IDOR) ou d'en exclure un si le nom affiché a changé depuis.
        // Ajouter une vraie FK est un changement de schéma/rétro-remplissage de données hors
        // du périmètre approuvé de ce lot — voir docs/audits/COSMECHIC-BUSINESS-POLICY-001.md.
        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            var userId = _userManager.GetUserId(User);
            _logger.LogInformation("User with ID '{UserId}' asked for their personal data.", userId);

            var profile = new Dictionary<string, string>();
            var personalDataProps = typeof(IdentityUser).GetProperties().Where(
                            prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
            foreach (var p in personalDataProps)
            {
                profile.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
            }

            var logins = await _userManager.GetLoginsAsync(user);
            var externalLogins = logins.Select(l => new
            {
                l.LoginProvider,
                ProviderKey = l.ProviderKey,
            }).ToList();

            var authenticatorKey = await _userManager.GetAuthenticatorKeyAsync(user);

            var addresses = await _businessContext.CustomerAddresses
                .Where(a => a.ApplicationUserId == userId)
                .Select(a => new
                {
                    a.Label,
                    a.RecipientName,
                    a.PhoneNumber,
                    a.StreetAddress,
                    a.City,
                    a.State,
                    a.PostalCode,
                    a.CountryCode,
                    a.IsDefaultShipping,
                })
                .ToListAsync();

            var orders = await _businessContext.OrderHeaders
                .Where(o => o.ApplicationUserId == userId)
                .Include(o => o.OrderDetails)
                .Select(o => new
                {
                    OrderId = o.Id,
                    o.OrderDate,
                    o.Subtotal,
                    o.ShippingAmount,
                    o.ShippingMethodName,
                    o.TaxAmount,
                    o.DiscountAmount,
                    o.OrderTotal,
                    o.RefundedAmount,
                    o.OrderStatus,
                    o.PaymentStatus,
                    o.FulfillmentStatus,
                    ShippingName = o.Name,
                    o.PhoneNumber,
                    o.StreetAddress,
                    o.City,
                    o.State,
                    o.PostalCode,
                    o.TrackingNumber,
                    o.Carrier,
                    o.ShippedAt,
                    o.DeliveredAt,
                    Items = o.OrderDetails.Select(d => new
                    {
                        d.ProduitNom,
                        d.Count,
                        d.Price,
                    }),
                })
                .ToListAsync();

            var returnRequests = await _businessContext.ReturnRequests
                .Where(rr => rr.ApplicationUserId == userId)
                .Include(rr => rr.Items)
                .Select(rr => new
                {
                    ReturnRequestId = rr.Id,
                    rr.OrderId,
                    rr.Status,
                    rr.Reason,
                    rr.CustomerComment,
                    rr.CreatedAt,
                    rr.ApprovedAt,
                    rr.ReceivedAt,
                    rr.CompletedAt,
                    Items = rr.Items.Select(ri => new
                    {
                        ri.OrderDetailId,
                        ri.Quantity,
                        ri.Reason,
                        ri.Restocked,
                    }),
                })
                .ToListAsync();

            var ownOrderIds = orders.Select(o => o.OrderId).ToList();
            var refunds = await _businessContext.Refunds
                .Where(r => ownOrderIds.Contains(r.OrderId))
                .Select(r => new
                {
                    r.OrderId,
                    r.ReturnRequestId,
                    r.Amount,
                    r.MerchandiseAmount,
                    r.ShippingAmount,
                    r.TaxAmount,
                    r.Cause,
                    r.Status,
                    r.Reason,
                    r.CreatedAt,
                    r.CompletedAt,
                })
                .ToListAsync();

            var export = new
            {
                Profile = profile,
                ExternalLogins = externalLogins,
                AuthenticatorKey = authenticatorKey,
                Addresses = addresses,
                Orders = orders,
                ReturnRequests = returnRequests,
                Refunds = refunds,
            };

            var json = JsonSerializer.SerializeToUtf8Bytes(export, new JsonSerializerOptions { WriteIndented = true });

            Response.Headers.Add("Content-Disposition", "attachment; filename=PersonalData.json");
            return new FileContentResult(json, "application/json");
        }
    }
}
