using Cosmechic.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cosmechic.Controllers
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 22) : administration minimale des taux de
    // taxe — liste, création, édition, désactivation (jamais de suppression physique : un
    // taux déjà utilisé pour calculer une commande historique doit rester consultable).
    // Le calcul lui-même (ITaxCalculator) ignore tout taux avec IsActive=false.
    [Authorize(Roles = "Admin")]
    public class TaxRatesController : Controller
    {
        private readonly CosmechicsContext _context;

        public TaxRatesController(CosmechicsContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _context.TaxRates.OrderBy(r => r.CountryCode).ThenBy(r => r.RegionCode).ToListAsync());
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Jurisdiction,CountryCode,RegionCode,Rate,EffectiveFrom,EffectiveTo")] TaxRate taxRate)
        {
            taxRate.Jurisdiction = taxRate.Jurisdiction?.Trim() ?? string.Empty;
            taxRate.CountryCode = taxRate.CountryCode?.Trim().ToUpperInvariant() ?? string.Empty;
            taxRate.RegionCode = string.IsNullOrWhiteSpace(taxRate.RegionCode) ? null : taxRate.RegionCode.Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(taxRate.Jurisdiction))
            {
                ModelState.AddModelError(nameof(TaxRate.Jurisdiction), "La juridiction est requise.");
            }

            if (string.IsNullOrEmpty(taxRate.CountryCode))
            {
                ModelState.AddModelError(nameof(TaxRate.CountryCode), "Le code pays est requis.");
            }

            if (taxRate.Rate < 0)
            {
                ModelState.AddModelError(nameof(TaxRate.Rate), "Le taux ne peut pas être négatif.");
            }

            if (taxRate.EffectiveTo.HasValue && taxRate.EffectiveTo.Value <= taxRate.EffectiveFrom)
            {
                ModelState.AddModelError(nameof(TaxRate.EffectiveTo), "La date de fin doit être postérieure à la date de début.");
            }

            if (ModelState.IsValid)
            {
                taxRate.IsActive = true;
                _context.Add(taxRate);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(taxRate);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var taxRate = await _context.TaxRates.FindAsync(id);
            if (taxRate == null)
            {
                return NotFound();
            }

            return View(taxRate);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("TaxRateId,Jurisdiction,CountryCode,RegionCode,Rate,EffectiveFrom,EffectiveTo,IsActive")] TaxRate posted)
        {
            if (id != posted.TaxRateId)
            {
                return NotFound();
            }

            posted.Jurisdiction = posted.Jurisdiction?.Trim() ?? string.Empty;
            posted.CountryCode = posted.CountryCode?.Trim().ToUpperInvariant() ?? string.Empty;
            posted.RegionCode = string.IsNullOrWhiteSpace(posted.RegionCode) ? null : posted.RegionCode.Trim().ToUpperInvariant();

            if (string.IsNullOrEmpty(posted.Jurisdiction))
            {
                ModelState.AddModelError(nameof(TaxRate.Jurisdiction), "La juridiction est requise.");
            }

            if (string.IsNullOrEmpty(posted.CountryCode))
            {
                ModelState.AddModelError(nameof(TaxRate.CountryCode), "Le code pays est requis.");
            }

            if (posted.Rate < 0)
            {
                ModelState.AddModelError(nameof(TaxRate.Rate), "Le taux ne peut pas être négatif.");
            }

            if (posted.EffectiveTo.HasValue && posted.EffectiveTo.Value <= posted.EffectiveFrom)
            {
                ModelState.AddModelError(nameof(TaxRate.EffectiveTo), "La date de fin doit être postérieure à la date de début.");
            }

            if (ModelState.IsValid)
            {
                var taxRate = await _context.TaxRates.FindAsync(id);
                if (taxRate == null)
                {
                    return NotFound();
                }

                taxRate.Jurisdiction = posted.Jurisdiction;
                taxRate.CountryCode = posted.CountryCode;
                taxRate.RegionCode = posted.RegionCode;
                taxRate.Rate = posted.Rate;
                taxRate.EffectiveFrom = posted.EffectiveFrom;
                taxRate.EffectiveTo = posted.EffectiveTo;
                taxRate.IsActive = posted.IsActive;

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(posted);
        }
    }
}
