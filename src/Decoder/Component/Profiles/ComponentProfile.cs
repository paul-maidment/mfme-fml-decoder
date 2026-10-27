using System.Collections.Generic;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Decoder.Component.Core;

namespace MfmeFmlDecoder.src.Decoder.Component.Profiles
{
    /// <summary>
    /// Declarative data for one (component, version) pair. Parsers hold behaviour only.
    /// There is no nearest-version fallback: every supported pair is registered explicitly.
    /// </summary>
    internal sealed record ComponentProfile(
        MfmeVersion Version,
        MFMEComponentType Kind,
        ComponentTagMap Tags,
        BaseSectionProfile BaseSection,
        AngleProfile Angle,
        ExtendedTagParser.Options Options,
        IReadOnlyList<string> ValueOrder,
        IReadOnlyList<string> ImageOrder,
        DefaultsProfile Defaults)
    {
        public ComponentTagMap NestedTags => Tags?.NestedTagBlockMap;

        public ComponentProfile WithVersion(MfmeVersion version) => this with { Version = version };

        public ComponentProfile WithOrientationDefault(string orientation)
        {
            return this with
            {
                Defaults = new DefaultsProfile(new Dictionary<string, object>()),
            };
        }

        public ComponentProfile WithOrientationRule(OrientationDefaultMode mode)
        {
            return this with
            {
                Defaults = new DefaultsProfile(new Dictionary<string, object>(), mode),
            };
        }

        public ComponentProfile WithDefaults(DefaultsProfile defaults)
        {
            return this with { Defaults = defaults };
        }

        public ComponentProfile WithTags(ComponentTagMap tags)
        {
            return this with { Tags = tags };
        }

        public ComponentProfile WithOptions(ExtendedTagParser.Options options)
        {
            return this with { Options = options };
        }

        public ComponentProfile AsLegacyTwoSection()
        {
            ComponentTagMap secondSectionTags = NestedTags;
            return this with
            {
                BaseSection = BaseSectionProfile.LegacyTwoSection(secondSectionTags),
            };
        }

        public ComponentProfile WithMode(BaseSectionMode mode)
        {
            return this with
            {
                BaseSection = mode == BaseSectionMode.V20Signature
                    ? BaseSectionProfile.V20Signature
                    : mode == BaseSectionMode.LegacyTwoSection
                        ? BaseSectionProfile.LegacyTwoSection(NestedTags)
                        : BaseSectionProfile.SingleStream,
            };
        }
    }
}
