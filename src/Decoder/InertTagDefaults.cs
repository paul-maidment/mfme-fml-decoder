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
            [MFMEComponentType.Lamp] = new uint[] { 0x0E, 0x3E },
            [MFMEComponentType.SevenSeg] = new uint[] { 0x24 },
            [MFMEComponentType.Led] = new uint[] { 0x1C },
            [MFMEComponentType.RGBLed] = new uint[] { 0x1C },
            [MFMEComponentType.DotAlpha] = new uint[] { 0x03 },
            [MFMEComponentType.FlipReel] = new uint[] { 0x09 },
            [MFMEComponentType.EpochDotAlpha] = new uint[] { 0x03, 0x09 },
            [MFMEComponentType.AceVideo] = new uint[] { 0x01, 0xE9 },
            [MFMEComponentType.DiscReel] = new uint[] { 0x06, 0x1C, 0x3E },
        };

        // 0x3B is a confirmed no-op on every kind.
        private const uint UniversalInertTag = 0x3B;

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
            ComponentProfile profile = ComponentProfileCatalog.Resolve(kind, MfmeVersion.V20_1);
            ComponentTagMap map = profile?.EffectiveClassTags;
            if (map is not null)
            {
                foreach (uint t in TagsFor(kind))
                {
                    if (map.TryGetValue(t, out TagInfo info) && !string.IsNullOrEmpty(info?.AttributeName))
                    {
                        names.Add(info.AttributeName);
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

