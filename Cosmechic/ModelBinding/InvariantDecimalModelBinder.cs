using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Cosmechic.ModelBinding
{
    /// <summary>
    /// Model binder opt-in explicite pour les propriétés <c>decimal</c>/<c>decimal?</c>
    /// alimentées par des formulaires HTML.
    ///
    /// Contexte (COSMECHIC-WINDOWS-CULTURE-TEST-REMEDIATION-001) : le binder par défaut
    /// d'ASP.NET Core pour <c>decimal</c> parse la valeur postée avec
    /// <see cref="CultureInfo.CurrentCulture"/> (culture ambiante du thread serveur). Un
    /// formulaire HTML (ex. &lt;input type="number"&gt;) transmet toujours une
    /// représentation numérique invariante (point décimal, RFC HTML), indépendamment de la
    /// culture/OS du serveur qui reçoit la requête. Sur un serveur configuré en fr-CA (ou
    /// toute culture à séparateur décimal virgule), "9.99" est alors rejeté par le binder
    /// par défaut — le même formulaire fonctionne ou échoue selon l'OS du serveur, ce que ce
    /// binder élimine pour les champs explicitement annotés.
    ///
    /// Portée délibérément limitée : appliqué uniquement via
    /// <c>[ModelBinder(BinderType = typeof(InvariantDecimalModelBinder))]</c> sur les
    /// propriétés qui en ont explicitement besoin. Non enregistré globalement (pas de
    /// <see cref="IModelBinderProvider"/> ajouté à la liste globale, aucune modification de
    /// Program.cs) — ne touche ni le binding de decimal en query string/route, ni celui d'un
    /// futur modèle non annoté. La résolution du type via l'attribut BinderType passe par le
    /// <c>BinderTypeModelBinderProvider</c> déjà enregistré par défaut dans MVC.
    ///
    /// Comportement :
    /// - "9.99", "15", "0.14975" (point décimal invariant) -> valeur decimal correspondante ;
    /// - chaîne vide/blanche sur <c>decimal?</c> -> null ;
    /// - chaîne vide/blanche sur <c>decimal</c> (non nullable) -> erreur ModelState (valeur requise) ;
    /// - entrée non parseable ("abc", virgule, séparateur de milliers, symbole monétaire, etc.)
    ///   -> erreur ModelState, jamais une exception, jamais une coercition silencieuse à 0.
    /// Ne réalise aucune opération arithmétique et ne modifie jamais la valeur après un parsing réussi.
    /// </summary>
    public sealed class InvariantDecimalModelBinder : IModelBinder
    {
        // Point décimal + signe + espaces de bordure uniquement. Explicitement AUCUN
        // NumberStyles.AllowThousands (pas de séparateur de milliers ambigu accepté) et
        // AUCUN NumberStyles.AllowCurrencySymbol (pas de forme localisée type "9,99 $").
        private const NumberStyles InvariantDecimalStyles =
            NumberStyles.AllowLeadingWhite
            | NumberStyles.AllowTrailingWhite
            | NumberStyles.AllowLeadingSign
            | NumberStyles.AllowDecimalPoint;

        public Task BindModelAsync(ModelBindingContext bindingContext)
        {
            if (bindingContext is null)
            {
                throw new ArgumentNullException(nameof(bindingContext));
            }

            var modelName = bindingContext.ModelName;
            var valueProviderResult = bindingContext.ValueProvider.GetValue(modelName);

            if (valueProviderResult == ValueProviderResult.None)
            {
                // Champ absent du POST : ne rien décider ici, comportement standard
                // (obligatoire/optionnel selon le type) laissé à la validation habituelle.
                return Task.CompletedTask;
            }

            bindingContext.ModelState.SetModelValue(modelName, valueProviderResult);

            var rawValue = valueProviderResult.FirstValue;
            var isNullableModel = Nullable.GetUnderlyingType(bindingContext.ModelType) != null;

            if (string.IsNullOrWhiteSpace(rawValue))
            {
                if (isNullableModel)
                {
                    bindingContext.Result = ModelBindingResult.Success(null);
                }
                else
                {
                    bindingContext.ModelState.TryAddModelError(
                        modelName,
                        $"Le champ {bindingContext.ModelMetadata.GetDisplayName()} est requis.");
                }

                return Task.CompletedTask;
            }

            if (decimal.TryParse(rawValue, InvariantDecimalStyles, CultureInfo.InvariantCulture, out var parsedValue))
            {
                bindingContext.Result = ModelBindingResult.Success(parsedValue);
            }
            else
            {
                bindingContext.ModelState.TryAddModelError(
                    modelName,
                    $"La valeur '{rawValue}' n'est pas un nombre décimal valide pour {bindingContext.ModelMetadata.GetDisplayName()}.");
            }

            return Task.CompletedTask;
        }
    }
}
