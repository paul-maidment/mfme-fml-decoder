using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Decoder.Component.Profiles;

namespace MfmeFmlDecoder.Decoder
{
    /// <summary>
    /// Component fields that are serialized by MFME but not consumed (verified by perturbation:
    /// changing them renders 0.000% identical, see docs/mfme-re/fml-unknown-tags.html). They are
    /// omitted from the decoder JSON and skipped by the writer, so the layout we produce never
    /// carries them regardless of whether the source did.
    /// </summary>
    internal static class InertTagDefaults
    {
        private static readonly Dictionary<MFMEComponentType, uint[]> PerKind = new()
        {
            [MFMEComponentType.Background] = new uint[] { 0x09, 0x0A, 0x0B },
            [MFMEComponentType.Border] = new uint[] { 0x04, 0x0A, 0x0B },
            [MFMEComponentType.MatrixAlpha] = new uint[] { 0x03, 0x0A },
            [MFMEComponentType.Bitmap] = new uint[] { 0x05 },
            // Lamp 0x3E (shape flag) and 0x0E (shape size, e.g. 0x32 for shape 7) are written by
            // MFME only for certain shapes and are NOT inert: dropping them changes the rendered
            // shape. They must be carried.
            [MFMEComponentType.SevenSeg] = new uint[] { 0x24 },
            [MFMEComponentType.Led] = new uint[] { 0x1C },
            [MFMEComponentType.RGBLed] = new uint[] { 0x1C },
            [MFMEComponentType.DotAlpha] = new uint[] { 0x03 },
            [MFMEComponentType.BandReel] = new uint[] { 0x11, 0x14, 0x15 },
            [MFMEComponentType.Reel] = new uint[] { 0x14, 0x1B, 0x1D, 0x22, 0x23 },
            [MFMEComponentType.FlipReel] = new uint[] { 0x09 },
            // EpochDotAlpha 0x03/0x09 stay inert: A/B verified - dropping them only (0x04/0x06/0x07/0x08
            // still carried) renders 0.000%. The Beaver Las Vegas fix came from the defaults, not these.
            [MFMEComponentType.EpochDotAlpha] = new uint[] { 0x03, 0x09 },
            [MFMEComponentType.AceVideo] = new uint[] { 0x01, 0xE9 },
            [MFMEComponentType.DiscReel] = new uint[] { 0x06, 0x1C, 0x3E },
        };

        // 0x3B is a confirmed no-op on every kind.
        private const uint UniversalInertTag = 0x3B;

        private static readonly MfmeVersion[] Versions =
        {
            MfmeVersion.V5, MfmeVersion.V6_1, MfmeVersion.V19_0, MfmeVersion.V19_8, MfmeVersion.V20_1,
        };

        private static readonly Dictionary<MFMEComponentType, IReadOnlySet<string>> NamesCache = new();

        public static IReadOnlySet<uint> TagsFor(MFMEComponentType kind)
        {
            var set = new HashSet<uint> { UniversalInertTag };
            if (PerKind.TryGetValue(kind, out uint[] tags))
            {
                foreach (uint t in tags)
                {
                    set.Add(t);
                }
            }

            return set;
        }

        public static IReadOnlySet<string> NamesFor(MFMEComponentType kind)
        {
            if (NamesCache.TryGetValue(kind, out IReadOnlySet<string> cached))
            {
                return cached;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (MfmeVersion version in Versions)
            {
                ComponentProfile profile = ComponentProfileCatalog.Resolve(kind, version);
                ComponentTagMap map = profile?.EffectiveClassTags;
                if (map is null)
                {
                    continue;
                }

                foreach (uint t in TagsFor(kind))
                {
                    if (map.TryGetValue(t, out TagInfo info) && !string.IsNullOrEmpty(info?.AttributeName))
                    {
                        // Outer/base spelling wins; fall back to the nested spelling only when the
                        // outer map does not carry the tag (so a same-id nested field such as
                        // Background 0x09 "TransparentColour" is never stripped).
                        names.Add(info.AttributeName);
                    }
                    else if (map.NestedTagBlockMap is { } nested
                        && nested.TryGetValue(t, out TagInfo nInfo)
                        && !string.IsNullOrEmpty(nInfo?.AttributeName))
                    {
                        names.Add(nInfo.AttributeName);
                    }
                }
            }

            NamesCache[kind] = names;
            return names;
        }

        /// <summary>Remove the inert fields from a layout JSON string (used by --json and --export).</summary>
        public static string FilterJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return json;
            }

            JsonNode root = JsonNode.Parse(json);
            if (root is not JsonObject obj || obj["Components"] is not JsonArray components)
            {
                return json;
            }

            foreach (JsonNode component in components)
            {
                if (component is not JsonObject componentObject
                    || componentObject["Type"]?.GetValue<string>() is not string type
                    || !Enum.TryParse(type, out MFMEComponentType kind))
                {
                    continue;
                }

                if (componentObject["Values"] is not JsonObject values)
                {
                    continue;
                }

                foreach (string name in NamesFor(kind))
                {
                    values.Remove(name);
                }
            }

            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}



