using System;

namespace MfmeFmlDecoder.Decoder
{
    internal enum ComponentOrderingMode
    {
        InFileOrder,
        BordersLastReversed,
    }

    /// <summary>
    /// File-level parsing state. The version is read once from TLV tag <c>0x2F</c>
    /// and flows through the walkers into every component parser. Component parsers
    /// never inspect version strings themselves.
    /// </summary>
    internal sealed class ParsingContext
    {
        public static readonly ParsingContext V20_1 =
            new ParsingContext(MfmeVersion.V20_1, sourcePath: null, allowUnsupportedVersion: false);

        public MfmeVersion Version { get; }

        public string SourcePath { get; }

        /// <summary>
        /// Diagnostic escape hatch only. It suppresses the file-level version gate;
        /// profile resolution still requires an explicit profile for the version.
        /// </summary>
        public bool AllowUnsupportedVersion { get; }

        public ComponentOrderingMode Ordering { get; }

        public ParsingContext(
            MfmeVersion version,
            string sourcePath,
            bool allowUnsupportedVersion,
            ComponentOrderingMode ordering = ComponentOrderingMode.InFileOrder)
        {
            Version = version;
            SourcePath = sourcePath;
            AllowUnsupportedVersion = allowUnsupportedVersion;
            Ordering = ordering;
        }

        public ParsingContext WithSourcePath(string sourcePath)
        {
            return new ParsingContext(Version, sourcePath, AllowUnsupportedVersion, Ordering);
        }
    }
}
