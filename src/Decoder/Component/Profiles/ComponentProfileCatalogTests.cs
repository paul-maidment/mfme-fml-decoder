using System.Linq;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Model;
using Xunit;

namespace MfmeFmlDecoder.src.Decoder.Component.Profiles
{
    public class ComponentProfileCatalogTests
    {
        [Fact]
        public void Catalogue_registers_explicit_profiles_for_both_supported_versions()
        {
            var registered = ComponentProfileCatalog.Registered.ToArray();

            Assert.Equal(78, registered.Length);
            Assert.All(registered, key =>
            {
                Assert.True(key.Version == MfmeVersion.V20_1 || key.Version == MfmeVersion.V19_8);
                Assert.NotEqual(MFMEComponentType.BaseClass, key.Kind);
            });
        }

        [Fact]
        public void Every_registered_kind_resolves_for_both_supported_versions()
        {
            MFMEComponentType[] kinds = ComponentProfileCatalog.Registered
                .Select(key => key.Kind)
                .Distinct()
                .ToArray();

            Assert.Equal(39, kinds.Length);
            foreach (MFMEComponentType kind in kinds)
            {
                Assert.True(ComponentProfileCatalog.TryResolve(kind, MfmeVersion.V20_1, out _), kind.ToString());
                Assert.True(ComponentProfileCatalog.TryResolve(kind, MfmeVersion.V19_8, out _), kind.ToString());
            }
        }

        [Fact]
        public void Missing_profile_version_fails_with_specific_error()
        {
            MfmeVersion unsupported = MfmeVersion.Parse("MFME v18.0");

            var ex = Assert.Throws<UnsupportedComponentProfileException>(() =>
                ComponentProfileCatalog.Resolve(MFMEComponentType.Reel, unsupported));

            Assert.Contains("No component profile registered for Reel", ex.Message);
            Assert.Contains("nearest-version fallback is not allowed", ex.Message);
        }

        [Theory]
        [InlineData("20.1", "20.1")]
        [InlineData("MFME v19.8", "19.8")]
        [InlineData("v19.8", "19.8")]
        [InlineData("20.0 Beta", "20.0")]
        [InlineData("MFME v6.0", "6.0")]
        [InlineData("v5", "5")]
        public void MfmeVersion_parses_embedded_version_strings(string raw, string expectedCanonical)
        {
            Assert.True(MfmeVersion.TryParse(raw, out MfmeVersion version));
            Assert.Equal(expectedCanonical, version.Canonical);
        }
    }
}
