// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Cosmechic.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cosmechic.Areas.Identity.Pages.Account.Manage
{
    public class DeletePersonalDataModel : PageModel
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly CosmechicsContext _businessContext;
        private readonly ILogger<DeletePersonalDataModel> _logger;

        public DeletePersonalDataModel(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            CosmechicsContext businessContext,
            ILogger<DeletePersonalDataModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _businessContext = businessContext;
            _logger = logger;
        }

        // COSMECHIC-ACCOUNT-001 (section 26) : la table AspNetUsers est physiquement
        // partagée entre ApplicationDbContext (Identity) et CosmechicsContext (commerce,
        // ARCH-002/DATA-001) — un OrderHeader porte une vraie contrainte FK vers cette
        // ligne (FK_OrderHeaders_AspNetUsers, DeleteBehavior.ClientSetNull mais
        // ApplicationUserId non-nullable ⇒ NO ACTION réel côté moteur). Avant ce correctif,
        // _userManager.DeleteAsync(user) sur un client ayant déjà commandé levait une
        // SqlException de contrainte FK non gérée (page en erreur 500), et pour un client
        // sans commande, ne posait pas de question — dans les deux cas, aucune décision
        // métier n'avait jamais été prise sur la politique de suppression. Politique
        // technique minimale retenue ici (pas de politique juridique inventée) :
        // suppression autorisée uniquement si aucun historique de commande n'existe.
        // Anonymisation/rétention minimale : TODO_REQUIRES_BUSINESS_CONFIGURATION.
        public bool HasOrderHistory { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public bool RequirePassword { get; set; }

        public async Task<IActionResult> OnGet()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            RequirePassword = await _userManager.HasPasswordAsync(user);
            HasOrderHistory = await _businessContext.OrderHeaders.AnyAsync(o => o.ApplicationUserId == user.Id);
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            HasOrderHistory = await _businessContext.OrderHeaders.AnyAsync(o => o.ApplicationUserId == user.Id);
            if (HasOrderHistory)
            {
                ModelState.AddModelError(string.Empty, "Ce compte a un historique de commandes et ne peut pas être supprimé pour le moment. Contactez le support pour toute demande liée à vos données.");
                RequirePassword = await _userManager.HasPasswordAsync(user);
                return Page();
            }

            RequirePassword = await _userManager.HasPasswordAsync(user);
            if (RequirePassword)
            {
                if (!await _userManager.CheckPasswordAsync(user, Input.Password))
                {
                    ModelState.AddModelError(string.Empty, "Incorrect password.");
                    return Page();
                }
            }

            var result = await _userManager.DeleteAsync(user);
            var userId = await _userManager.GetUserIdAsync(user);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Unexpected error occurred deleting user.");
            }

            await _signInManager.SignOutAsync();

            _logger.LogInformation("User with ID '{UserId}' deleted themselves.", userId);

            return Redirect("~/");
        }
    }
}
