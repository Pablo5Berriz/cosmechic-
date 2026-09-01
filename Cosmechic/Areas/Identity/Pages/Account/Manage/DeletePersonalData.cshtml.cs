// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Cosmechic.Models;
using Cosmechic.Services;
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
        private readonly IAccountAnonymizationService _anonymizationService;
        private readonly ILogger<DeletePersonalDataModel> _logger;

        public DeletePersonalDataModel(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            CosmechicsContext businessContext,
            IAccountAnonymizationService anonymizationService,
            ILogger<DeletePersonalDataModel> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _businessContext = businessContext;
            _anonymizationService = anonymizationService;
            _logger = logger;
        }

        // COSMECHIC-ACCOUNT-001 (section 26) : la table AspNetUsers est physiquement
        // partagée entre ApplicationDbContext (Identity) et CosmechicsContext (commerce,
        // ARCH-002/DATA-001) — un OrderHeader porte une vraie contrainte FK vers cette
        // ligne (FK_OrderHeaders_AspNetUsers, DeleteBehavior.ClientSetNull mais
        // ApplicationUserId non-nullable ⇒ NO ACTION réel côté moteur), d'où l'impossibilité
        // d'un hard-delete pour un client ayant déjà commandé.
        // COSMECHIC-BUSINESS-POLICY-001 (section 7) : ACCOUNT_DELETION_ANONYMIZATION_POLICY
        // approuvée par le PM — HasOrderHistory=true déclenche désormais une anonymisation
        // (IAccountAnonymizationService) plutôt qu'un blocage pur. HasOrderHistory=false
        // continue de déclencher un hard-delete réel (inchangé, rien à préserver).
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

            RequirePassword = await _userManager.HasPasswordAsync(user);
            if (RequirePassword)
            {
                if (!await _userManager.CheckPasswordAsync(user, Input.Password))
                {
                    ModelState.AddModelError(string.Empty, "Incorrect password.");
                    return Page();
                }
            }

            var userId = await _userManager.GetUserIdAsync(user);

            if (HasOrderHistory)
            {
                // COSMECHIC-BUSINESS-POLICY-001 (section 7) : anonymisation plutôt que
                // blocage — voir AccountAnonymizationService pour ce qui est réellement
                // anonymisé/conservé.
                var anonymized = await _anonymizationService.AnonymizeAsync(userId);
                if (!anonymized)
                {
                    throw new InvalidOperationException("Unexpected error occurred anonymizing user.");
                }

                await _signInManager.SignOutAsync();
                _logger.LogInformation("User with ID '{UserId}' anonymized their account (order history preserved).", userId);
                return Redirect("~/");
            }

            var result = await _userManager.DeleteAsync(user);
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
