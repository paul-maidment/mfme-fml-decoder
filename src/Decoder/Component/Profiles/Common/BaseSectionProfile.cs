using MfmeFmlDecoder.Model;

namespace MfmeFmlDecoder.src.Decoder.Component.Profiles
{
    internal enum BaseSectionMode
    {
        /// <summary>Legacy single extended-tag stream.</summary>
        SingleStream,

        /// <summary>
        /// 20.1 files whose base section is recognised by a per-component signature and
        /// terminated by a <c>4C ?? 00</c> nested-tag opener.
        /// </summary>
        V20Signature,

        /// <summary>
        /// Legacy layouts that store the base section and the own section as two TLV
        /// streams separated by a single <c>0x00</c>.
        /// </summary>
        LegacyTwoSection,
    }

    internal sealed record BaseSectionProfile(
        BaseSectionMode Mode,
        ComponentTagMap SecondSectionTags = null)
    {
        public static readonly BaseSectionProfile SingleStream =
            new(BaseSectionMode.SingleStream);

        public static readonly BaseSectionProfile V20Signature =
            new(BaseSectionMode.V20Signature);

        public static BaseSectionProfile LegacyTwoSection(ComponentTagMap secondSectionTags)
        {
            return new BaseSectionProfile(BaseSectionMode.LegacyTwoSection, secondSectionTags);
        }
    }
}
