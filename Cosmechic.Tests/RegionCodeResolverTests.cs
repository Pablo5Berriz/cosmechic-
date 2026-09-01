using Cosmechic.Services;
using Xunit;

namespace Cosmechic.Tests
{
    // COSMECHIC-COMMERCE-OPERATIONS-001A (section 47) : normalisation minimale et sûre du
    // champ libre "province" — jamais d'appel externe, jamais de taxonomie provinciale
    // complète inventée.
    public class RegionCodeResolverTests
    {
        [Theory]
        [InlineData("QC")]
        [InlineData("qc")]
        [InlineData("Quebec")]
        [InlineData("québec")]
        [InlineData("QUÉBEC")]
        [InlineData("  QC  ")]
        public void QuebecVariants_AllResolveToQC(string input)
        {
            Assert.Equal("QC", RegionCodeResolver.ResolveCanadianRegionCode(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NullOrBlank_ResolvesToNull(string? input)
        {
            Assert.Null(RegionCodeResolver.ResolveCanadianRegionCode(input));
        }

        [Fact]
        public void TwoLetterCode_PassesThroughUppercased()
        {
            Assert.Equal("ON", RegionCodeResolver.ResolveCanadianRegionCode("on"));
        }

        [Fact]
        public void FreeTextNonTwoLetterNonQuebec_ResolvesToNull()
        {
            Assert.Null(RegionCodeResolver.ResolveCanadianRegionCode("Ontario"));
        }
    }
}
