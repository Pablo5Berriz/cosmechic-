using System.Net.Http;
using System.Threading.Tasks;
using Cosmechic.Tests.Infrastructure;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-LEGAL-READINESS-001 (section 2) : BRAND_PRIMARY=#cb350b approuvé par le PM
    // (contraste 5.18:1 sur blanc, seuil AA 4.5:1). Remplacement CIBLÉ — jamais un
    // remplacement aveugle global : les deux usages purement décoratifs/non-interactifs
    // (barre de progression, ligne de séparation hr.divider) conservent volontairement
    // l'ancienne couleur de marque #f4623a, documenté dans
    // docs/audits/COSMECHIC-LEGAL-READINESS-001.md. Toutes les autres occurrences — texte de
    // lien, bouton primaire (plein et outline), badges/éléments actifs de dropdown/
    // pagination/nav-pills/list-group, cases à cocher/curseurs interactifs, survol/actif de
    // navigation — sont passées à la nouvelle couleur.
    //
    // Les assertions portent sur le fichier réellement SERVI (via le pipeline HTTP complet,
    // comme un navigateur le recevrait), pas seulement le fichier source sur disque — même
    // patron que FrontendSelfHostingTests.cs (COSMECHIC-RELEASE-CONFIG-001).
    public class BrandContrastTests : IDisposable
    {
        private readonly CustomWebApplicationFactory _factory = new();

        private const string OldBrandPrimary = "#f4623a";
        private const string NewBrandPrimary = "#cb350b";

        private async Task<string> GetStylesCssAsync()
        {
            var client = _factory.CreateTestClient().AsAnonymous();
            var response = await client.GetAsync("/css/styles.css");
            return await response.Content.ReadAsStringAsync();
        }

        [Fact]
        public async Task RootTokens_PrimaryAndLinkColor_UseApprovedBrandColor()
        {
            var css = await GetStylesCssAsync();

            Assert.Contains($"--bs-primary: {NewBrandPrimary};", css);
            Assert.Contains($"--bs-link-color: {NewBrandPrimary};", css);
            Assert.Contains($"--bs-orange: {NewBrandPrimary};", css);
        }

        [Fact]
        public async Task PrimaryButton_BackgroundBorderAndDisabledState_UseApprovedBrandColor()
        {
            var css = await GetStylesCssAsync();

            Assert.Contains(".btn-primary {", css);
            var btnPrimaryBlock = css.Substring(css.IndexOf(".btn-primary {"));
            btnPrimaryBlock = btnPrimaryBlock.Substring(0, btnPrimaryBlock.IndexOf('}'));

            Assert.Contains($"--bs-btn-bg: {NewBrandPrimary};", btnPrimaryBlock);
            Assert.Contains($"--bs-btn-border-color: {NewBrandPrimary};", btnPrimaryBlock);
            // États disabled explicitement vérifiés (section 2 de la directive).
            Assert.Contains($"--bs-btn-disabled-bg: {NewBrandPrimary};", btnPrimaryBlock);
            Assert.Contains($"--bs-btn-disabled-border-color: {NewBrandPrimary};", btnPrimaryBlock);
        }

        [Fact]
        public async Task OutlinePrimaryButton_TextColorAndHoverActiveStates_UseApprovedBrandColor()
        {
            var css = await GetStylesCssAsync();

            Assert.Contains(".btn-outline-primary {", css);
            var block = css.Substring(css.IndexOf(".btn-outline-primary {"));
            block = block.Substring(0, block.IndexOf(".btn-outline-secondary"));

            Assert.Contains($"--bs-btn-color: {NewBrandPrimary};", block); // texte sur fond blanc
            Assert.Contains($"--bs-btn-border-color: {NewBrandPrimary};", block);
            Assert.Contains($"--bs-btn-hover-bg: {NewBrandPrimary};", block);
            Assert.Contains($"--bs-btn-active-bg: {NewBrandPrimary};", block);
            Assert.Contains($"--bs-btn-disabled-color: {NewBrandPrimary};", block);
        }

        [Fact]
        public async Task ActiveInteractiveStates_DropdownPaginationNavPillsListGroup_UseApprovedBrandColor()
        {
            var css = await GetStylesCssAsync();

            Assert.Contains($"--bs-dropdown-link-active-bg: {NewBrandPrimary};", css);
            Assert.Contains($"--bs-nav-pills-link-active-bg: {NewBrandPrimary};", css);
            Assert.Contains($"--bs-pagination-active-bg: {NewBrandPrimary};", css);
            Assert.Contains($"--bs-list-group-active-bg: {NewBrandPrimary};", css);
        }

        [Fact]
        public async Task LinkPrimaryUtilityAndNavHoverActiveText_UseApprovedBrandColor()
        {
            var css = await GetStylesCssAsync();

            Assert.Contains(".link-primary {", css);
            var linkPrimaryBlock = css.Substring(css.IndexOf(".link-primary {"));
            linkPrimaryBlock = linkPrimaryBlock.Substring(0, linkPrimaryBlock.IndexOf('}'));
            Assert.Contains($"color: {NewBrandPrimary} !important;", linkPrimaryBlock);

            Assert.Contains("#mainNav .navbar-nav .nav-item .nav-link:hover, #mainNav .navbar-nav .nav-item .nav-link:active {", css);
        }

        [Fact]
        public async Task DecorativeNonInteractiveElements_KeepOriginalBrandAccent_NotBlindGlobalReplace()
        {
            var css = await GetStylesCssAsync();

            // Barre de progression (information, pas texte/lien/bouton/contrôle interactif) :
            // conservée intentionnellement — preuve que le remplacement n'a pas été aveugle.
            Assert.Contains($"--bs-progress-bar-bg: {OldBrandPrimary};", css);
            // hr.divider (ligne de séparation décorative) : conservée intentionnellement.
            Assert.Contains("hr.divider {", css);
        }

        [Theory]
        [InlineData("#cb350b", "#ffffff", 4.5)] // texte/lien sur fond blanc.
        [InlineData("#ffffff", "#cb350b", 4.5)] // texte blanc sur bouton primaire.
        public void BrandPrimary_MeetsWcagAaNormalTextContrast(string foreground, string background, double requiredRatio)
        {
            var ratio = WcagContrast.Ratio(foreground, background);

            Assert.True(ratio >= requiredRatio, $"Contraste {foreground} sur {background} = {ratio:0.00}:1, sous le seuil AA {requiredRatio}:1.");
        }

        public void Dispose() => _factory.Dispose();
    }

    // Implémentation directe de la formule de luminance relative WCAG 2.x — utilisée pour
    // recalculer le ratio dans le test plutôt que de coder en dur une valeur déjà annoncée
    // par le PM, afin que ce test échoue réellement si la couleur approuvée changeait sans
    // que le contraste ait été revérifié.
    internal static class WcagContrast
    {
        public static double Ratio(string hexForeground, string hexBackground)
        {
            var l1 = RelativeLuminance(hexForeground);
            var l2 = RelativeLuminance(hexBackground);
            var lighter = Math.Max(l1, l2);
            var darker = Math.Min(l1, l2);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(string hex)
        {
            hex = hex.TrimStart('#');
            var r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255.0;
            var g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0;
            var b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0;
            return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
        }

        private static double Linearize(double channel) =>
            channel <= 0.03928 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
