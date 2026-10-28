using System.Linq;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Model;
using Xunit;

namespace MfmeFmlDecoder.src.Decoder.Component.Profiles
{
    public class ComponentProfileCatalogTests
    {
        private static readonly MfmeVersion[] SupportedVersions =
        {
            MfmeVersion.V20_1,
            MfmeVersion.V20_0,
            MfmeVersion.V19_8,
            MfmeVersion.V19_6,
            MfmeVersion.V19_5,
            MfmeVersion.V19_4,
            MfmeVersion.V19_3,
            MfmeVersion.V19_2,
            MfmeVersion.V19_1,
            MfmeVersion.V19_0,
            MfmeVersion.V6_1,
            MfmeVersion.V6_0,
            MfmeVersion.V5_2,
            MfmeVersion.V5,
            MfmeVersion.V18_0,
            MfmeVersion.MFME,
        };

        [Fact]
        public void Catalogue_registers_explicit_profiles_for_every_supported_version()
        {
            var registered = ComponentProfileCatalog.Registered.ToArray();

            Assert.Equal(624, registered.Length);
            Assert.All(registered, key =>
            {
                Assert.Contains(key.Version, SupportedVersions);
                Assert.NotEqual(MFMEComponentType.BaseClass, key.Kind);
            });
        }

        [Fact]
        public void Every_registered_kind_resolves_for_every_supported_version()
        {
            MFMEComponentType[] kinds = ComponentProfileCatalog.Registered
                .Select(key => key.Kind)
                .Distinct()
                .ToArray();

            Assert.Equal(39, kinds.Length);
            foreach (MFMEComponentType kind in kinds)
            {
                foreach (MfmeVersion version in SupportedVersions)
                {
                    Assert.True(
                        ComponentProfileCatalog.TryResolve(kind, version, out _),
                        $"{kind} @ {version}");
                }
            }
        }

        [Fact]
        public void Missing_profile_version_fails_with_specific_error()
        {
            MfmeVersion unsupported = MfmeVersion.Parse("MFME v21.0");

            var ex = Assert.Throws<UnsupportedComponentProfileException>(() =>
                ComponentProfileCatalog.Resolve(MFMEComponentType.Reel, unsupported));

            Assert.Contains("No component profile registered for Reel", ex.Message);
            Assert.Contains("nearest-version fallback is not allowed", ex.Message);
        }

        [Theory]
        [InlineData("20.1", "20.1")]
        [InlineData("MFME v20.0", "20.0")]
        [InlineData("MFME v19.8", "19.8")]
        [InlineData("MFME v19.6", "19.6")]
        [InlineData("MFME v19.0", "19.0")]
        [InlineData("v19.8", "19.8")]
        [InlineData("20.0 Beta", "20.0")]
        [InlineData("MFME v6.0", "6.0")]
        [InlineData("v5", "5")]
        [InlineData("MFME v5.2", "5.2")]
        [InlineData("MFME v18.0", "18.0")]
        [InlineData("MFME", "MFME")]
        public void MfmeVersion_parses_embedded_version_strings(string raw, string expectedCanonical)
        {
            Assert.True(MfmeVersion.TryParse(raw, out MfmeVersion version));
            Assert.Equal(expectedCanonical, version.Canonical);
        }
    }
}
