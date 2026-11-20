using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Decoder.Writer;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Decoder.Component.Profiles;
using MfmeFmlDecoder.src.Model;
using MfmeFmlDecoder.src.Model.Component;

namespace MfmeFmlDecoder.Utilities
{
    /// <summary>
    /// Rebuilds the <see cref="Layout"/> model from an <c>--export</c> zip: the
    /// <c>layout.json</c> component tree plus the <c>images/</c> blobs. This is the reverse
    /// of <see cref="LayoutExporter"/> for the fields the JSON model carries.
    ///
    /// The typed value dictionaries (<c>Strings</c>, <c>UInt32s</c>, …) are <c>[JsonIgnore]</c>
    /// and flattened into a single <c>Values</c> object, so each key is routed back to its
    /// dictionary using the component's 20.1 tag map role. Raw/unknown tag bytes are absent
    /// from the JSON by design; the render gate is what validates their loss.
    /// </summary>
    internal static class LayoutImporter
    {
        public static Layout ImportFromZip(string zipPath)
        {
            if (string.IsNullOrWhiteSpace(zipPath)) throw new ArgumentException("Zip path required.", nameof(zipPath));
            if (!File.Exists(zipPath)) throw new FileNotFoundException("Export zip not found.", zipPath);

            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            string layoutJson = ReadEntryText(zip, "layout.json");
            var images = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                if (entry.FullName.StartsWith("images/", StringComparison.Ordinal) &&
                    entry.Name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                {
                    images[entry.FullName] = ReadEntryBytes(entry);
                }
            }

            return Import(layoutJson, images);
        }

        public static Layout Import(string layoutJson, IReadOnlyDictionary<string, byte[]> imagesByPath)
        {
            if (layoutJson is null) throw new ArgumentNullException(nameof(layoutJson));
            imagesByPath ??= new Dictionary<string, byte[]>();

            JsonNode root = JsonNode.Parse(layoutJson) ?? throw new InvalidDataException("layout.json is empty.");
            JsonArray componentsNode = root["Components"] as JsonArray
                ?? throw new InvalidDataException("layout.json has no Components array.");

            var components = new List<BaseComponent>(componentsNode.Count);
            for (int ordinal = 0; ordinal < componentsNode.Count; ordinal++)
            {
                JsonObject node = componentsNode[ordinal] as JsonObject
                    ?? throw new InvalidDataException($"Component {ordinal} is not an object.");
                components.Add(ImportComponent(node, ordinal, imagesByPath));
            }

            var header = new LayoutFileHeader();
            if (imagesByPath.TryGetValue("images/" + LayoutFileHeader.SplashBitmapImageKey + ".bmp", out byte[] splash) &&
                splash is { Length: > 0 })
            {
                BmpUtility.BmpInfo info = BmpUtility.ReadInfo(splash);
                header.SplashBitmap = new BitmapEntry(info.Width, info.Height, info.BitsPerPixel, splash)
                {
                    Purpose = LayoutFileHeader.SplashBitmapImageKey
                };
            }

            return new Layout(components, header);
        }

        private static BaseComponent ImportComponent(JsonObject node, int ordinal, IReadOnlyDictionary<string, byte[]> images)
        {
            string typeName = node["Type"]?.GetValue<string>();
            BaseComponent component = ComponentFactory.Create(typeName);

            // Top-level scalar properties (e.g. Border.BoxX, BFMAlpha.OffLevel, BandReel.MaskSlots)
            // round-trip through the source-generated JSON context; the flattened Values and the
            // geometry/derived blocks are applied separately below.
            JsonObject props = (JsonObject)node.DeepClone();
            props.Remove("Type");
            props.Remove("OrdinalComponentIdentifier");
            props.Remove("Geometry");
            props.Remove("Images");
            props.Remove("Values");
            props.Remove("Fonts");
            props.Remove("SubLampNumberTable");
            props.Remove("AngleFlag");
            if (props.Count > 0)
            {
                JsonTypeInfo typeInfo = ComponentJsonContext.Default.GetTypeInfo(component.GetType())
                    ?? throw new InvalidDataException($"No JSON type info for component '{typeName}'.");
                if (JsonSerializer.Deserialize(props.ToJsonString(), typeInfo) is BaseComponent populated)
                {
                    component = populated;
                }
            }

            MFMEComponentType kind = ComponentKind.Resolve(component);
            ApplyGeometry(node["Geometry"] as JsonObject, component);
            if (node["AngleFlag"] is JsonValue angleFlag && angleFlag.TryGetValue(out int flagValue))
            {
                component.GeometryAngleFlag = (byte)flagValue;
            }
            ApplyFonts(node["Fonts"] as JsonObject, component);
            ApplySublampTable(node["SubLampNumberTable"] as JsonObject, component);
            ApplyValues(node["Values"] as JsonObject, kind, component);
            ApplyImages(node["Images"] as JsonObject, ordinal, images, component);
            ApplyDerivedProperties(node, component);
            ApplySegmentLamps(node["SegmentLamps"], component);
            ApplyDefaultSubLampTable(component);

            component.OrdinalComponentIdentifier = ordinal;
            return component;
        }

        private static void ApplyGeometry(JsonObject geometry, BaseComponent component)
        {
            if (geometry is null)
            {
                return;
            }

            component.X = ReadInt(geometry["X"]);
            component.Y = ReadInt(geometry["Y"]);
            component.Width = (uint)ReadInt(geometry["Width"]);
            component.Height = (uint)ReadInt(geometry["Height"]);
            component.Number = ReadInt(geometry["Number"]);
            component.Angle = (float)ReadDouble(geometry["Angle"]);
            if (geometry["Orientation"] is JsonNode orientation)
            {
                component.Orientation = orientation.GetValue<string>();
            }
        }

        private static void ApplyFonts(JsonObject fonts, BaseComponent component)
        {
            if (fonts is null)
            {
                return;
            }

            foreach (KeyValuePair<string, JsonNode> kv in fonts)
            {
                if (kv.Value is not JsonObject f)
                {
                    continue;
                }

                component.Fonts[kv.Key] = new FontTagEntry(
                    (uint)ReadInt(f["Tag"]),
                    f["Role"]?.GetValue<string>(),
                    f["FontName"]?.GetValue<string>(),
                    (uint)ReadInt(f["FontSize"]),
                    (byte)ReadInt(f["ScriptStyleRaw"]),
                    f["ScriptStyleName"]?.GetValue<string>(),
                    f["TextColour"]?.GetValue<string>(),
                    (byte)ReadInt(f["FontStyle"]));
            }
        }

        // MFME writes every component's sub-lamp slots, filling undefined ones with -2. The slot
        // count is a fixed property of the component type (it does not vary per layout), so the
        // table is sized here from the type rather than serialized. The JSON only lists the slots
        // that carry a lamp number; the rest are implied defaults.
        private static readonly Dictionary<MFMEComponentType, int> SublampSlotCounts = new()
        {
            [MFMEComponentType.Button] = 2,
            [MFMEComponentType.Lamp] = 12,
            [MFMEComponentType.Label] = 1,
            [MFMEComponentType.Led] = 1,
            [MFMEComponentType.FlipReel] = 3,
            [MFMEComponentType.BandReel] = 60,
        };

        private static void ApplySublampTable(JsonObject table, BaseComponent component)
        {
            if (table is null)
            {
                return;
            }

            var defined = new Dictionary<int, int>();
            foreach (KeyValuePair<string, JsonNode> kv in table)
            {
                if (!kv.Key.StartsWith("Lamp", StringComparison.Ordinal) || kv.Value is null)
                {
                    continue;
                }

                if (int.TryParse(kv.Key.Substring(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                {
                    defined[index] = ReadInt(kv.Value);
                }
            }

            // The model table is a slot-ordered list (1..N) with -2 for undefined slots; the
            // JSON only lists defined ones. The declared count is the full slot count, so size by
            // it too, otherwise a trailing -2 slot is lost. Lamps are a fixed 12 slots.
            uint count = table["NumberOfDefinedLampNumbers"] is JsonNode countNode
                ? unchecked((uint)ReadLong(countNode))
                : 0u;
            bool hasCount = table["NumberOfDefinedLampNumbers"] is not null;

            int maxIndex = SublampSlotCounts.TryGetValue(ComponentKind.Resolve(component), out int fixedSlots)
                ? fixedSlots
                : 0;
            if (hasCount && count > (uint)maxIndex)
            {
                maxIndex = (int)count;
            }

            foreach (int index in defined.Keys)
            {
                if (index > maxIndex)
                {
                    maxIndex = index;
                }
            }

            var entries = new List<LampSublampTableEntry>(maxIndex);
            for (int slot = 1; slot <= maxIndex; slot++)
            {
                entries.Add(new LampSublampTableEntry(slot, defined.TryGetValue(slot, out int value) ? value : -2));
            }

            switch (component)
            {
                case Reel reel:
                    reel.SublampTable = entries;
                    if (hasCount) component.UInt32s["NonNullSublampCount"] = count;
                    break;
                case Button button:
                    button.SublampTable = entries;
                    if (hasCount) component.UInt32s["NonNullSublampCount"] = count;
                    break;
                case Lamp lamp:
                    lamp.SublampTable = entries;
                    if (hasCount) component.UInt32s["NumberOfDefinedLampNumbers"] = count;
                    break;
                case DiscReel discReel:
                    discReel.SublampTable = entries;
                    break;
            }
        }

        private static void ApplyValues(JsonObject values, MFMEComponentType kind, BaseComponent component)
        {
            if (values is null)
            {
                return;
            }

            Dictionary<string, ValueRole> roles = BuildRoleMap(kind);
            foreach (KeyValuePair<string, JsonNode> kv in values)
            {
                if (kv.Value is null)
                {
                    continue;
                }

                ValueRole role = ResolveRole(kv.Key, roles, kv.Value);
                RouteValue(component, kv.Key, kv.Value, role);
            }
        }

        // Values the decoder derives into typed dictionaries from a raw tag (so their names are
        // not in the tag map). Routing them correctly is what keeps the derived tag rebuildable.
        private static readonly Dictionary<string, ValueRole> DerivedKeyRoles = new(StringComparer.Ordinal)
        {
            ["BoxX"] = ValueRole.INT32,
            ["BoxY"] = ValueRole.INT32,
            ["LeftWidth"] = ValueRole.INT32,
            ["TopHeight"] = ValueRole.INT32,
            ["RightWidth"] = ValueRole.INT32,
            ["BottomHeight"] = ValueRole.INT32,
            ["BoxWidth"] = ValueRole.UINT32,
            ["BoxHeight"] = ValueRole.UINT32,
            ["WidthDiff"] = ValueRole.INT32,
            ["RedLampNumber"] = ValueRole.INT32,
            ["GreenLampNumber"] = ValueRole.INT32,
            ["BlueLampNumber"] = ValueRole.INT32,
            ["WhiteLampNumber"] = ValueRole.INT32,
        };

        private static ValueRole ResolveRole(string name, Dictionary<string, ValueRole> roles, JsonNode value)
        {
            if (roles.TryGetValue(name, out ValueRole mapped))
            {
                return mapped;
            }

            if (DerivedKeyRoles.TryGetValue(name, out ValueRole derived))
            {
                return derived;
            }

            // FlipReel / PrismLamp per-index lamp numbers.
            if (name.StartsWith("SubLamp", StringComparison.Ordinal) && name.EndsWith("Number", StringComparison.Ordinal))
            {
                return ValueRole.UINT32;
            }

            return InferRole(value);
        }

        /// <summary>
        /// Sets model properties the writer reads for raw-backed tags (Border bounding box /
        /// pane split, BandReel mask slots) from their JSON representation, which is flattened or
        /// nested rather than a tag.
        /// </summary>
        private static void ApplyDerivedProperties(JsonObject node, BaseComponent component)
        {
            if (component is Border border)
            {
                if (border.Int32s.TryGetValue("BoxX", out int absX)) border.BoxX = absX - border.X;
                if (border.Int32s.TryGetValue("BoxY", out int absY)) border.BoxY = absY - border.Y;
                if (border.UInt32s.TryGetValue("BoxWidth", out uint bw)) border.BoxWidth = bw;
                if (border.UInt32s.TryGetValue("BoxHeight", out uint bh)) border.BoxHeight = bh;
                if (border.Int32s.TryGetValue("LeftWidth", out int lw)) border.LeftWidth = lw;
                if (border.Int32s.TryGetValue("TopHeight", out int th)) border.TopHeight = th;
                if (border.Int32s.TryGetValue("RightWidth", out int rw)) border.RightWidth = rw;
                if (border.Int32s.TryGetValue("BottomHeight", out int bhh)) border.BottomHeight = bhh;
            }

            if (component is BandReel bandReel && node["Masks"] is JsonObject masks)
            {
                foreach (KeyValuePair<string, JsonNode> mask in masks)
                {
                    if (mask.Value is not JsonObject slots || !bandReel.MaskSlots.Masks.TryGetValue(mask.Key, out var target))
                    {
                        continue;
                    }

                    foreach (KeyValuePair<string, JsonNode> slot in slots)
                    {
                        if (target.TryGetValue(slot.Key, out SlotValue sv))
                        {
                            sv.SublampNumber = ReadInt(slot.Value);
                        }
                    }
                }
            }
        }

        private static void RouteValue(BaseComponent component, string name, JsonNode value, ValueRole role)
        {
            switch (role)
            {
                case ValueRole.UINT32:
                    component.UInt32s[name] = unchecked((uint)ReadLong(value));
                    break;
                case ValueRole.INT32:
                    component.Int32s[name] = unchecked((int)ReadLong(value));
                    break;
                case ValueRole.UINT16:
                    component.UInt16s[name] = unchecked((ushort)ReadLong(value));
                    break;
                case ValueRole.BYTE:
                    component.Bytes[name] = unchecked((byte)ReadLong(value));
                    break;
                case ValueRole.BOOLEAN:
                    component.Booleans[name] = ReadBool(value);
                    break;
                case ValueRole.FLOAT:
                    component.Floats[name] = (float)ReadDouble(value);
                    break;
                case ValueRole.ARGB_COLOR:
                    component.Colours[name] = value.GetValue<string>();
                    break;
                case ValueRole.TEXT:
                    component.Strings[name] = value.GetValue<string>();
                    break;
                default:
                    RouteByJsonKind(component, name, value);
                    break;
            }
        }

        private static void RouteByJsonKind(BaseComponent component, string name, JsonNode value)
        {
            if (value is not JsonValue jsonValue)
            {
                return;
            }

            if (jsonValue.TryGetValue(out bool b))
            {
                component.Booleans[name] = b;
            }
            else if (jsonValue.TryGetValue(out string s))
            {
                if (s.Length == 9 && s[0] == '#')
                {
                    component.Colours[name] = s;
                }
                else
                {
                    component.Strings[name] = s;
                }
            }
            else if (jsonValue.TryGetValue(out long l))
            {
                component.UInt32s[name] = unchecked((uint)l);
            }
            else if (jsonValue.TryGetValue(out double d))
            {
                component.Floats[name] = (float)d;
            }
        }

        private static Dictionary<string, ValueRole> BuildRoleMap(MFMEComponentType kind)
        {
            var roles = new Dictionary<string, ValueRole>(StringComparer.Ordinal);
            if (!ComponentProfileCatalog.TryResolve(kind, MfmeVersion.V20_1, out ComponentProfile profile))
            {
                return roles;
            }

            AddMap(roles, profile.EffectiveClassTags);
            AddMap(roles, profile.EffectiveClassTags?.NestedTagBlockMap);
            AddMap(roles, profile.EffectiveBaseTags);
            return roles;
        }

        private static void AddMap(Dictionary<string, ValueRole> roles, ComponentTagMap map)
        {
            if (map is null)
            {
                return;
            }

            foreach (KeyValuePair<uint, TagInfo> entry in map)
            {
                string name = entry.Value?.AttributeName;
                if (!string.IsNullOrEmpty(name) && !roles.ContainsKey(name))
                {
                    roles[name] = entry.Value.Role;
                }
            }
        }

        /// <summary>
        /// MFME 20.1 always writes a SevenSeg sub-lamp table: a fixed 16-slot table where an
        /// unwired slot is -2. layout.json drops it when every slot is undefined (SegmentLamps
        /// has no positive entries), so re-supply the standard all-(-2) table. A display in
        /// sub-lamp mode with no bound lamps is not the same as having no table at all.
        /// </summary>
        /// <summary>
        /// SevenSeg serialises its sub-lamp table as a flat <c>SegmentLamps</c> array (the defined
        /// lamp numbers in slot order). Rebuild the slot-indexed table from it.
        /// </summary>
        private static void ApplySegmentLamps(JsonNode node, BaseComponent component)
        {
            if (component is not SevenSeg sevenSeg || sevenSeg.SublampTable is { Count: > 0 })
            {
                return;
            }

            if (node is not JsonArray lamps || lamps.Count == 0)
            {
                return;
            }

            var table = new List<LampSublampTableEntry>(lamps.Count);
            for (int i = 0; i < lamps.Count; i++)
            {
                table.Add(new LampSublampTableEntry(i + 1, ReadInt(lamps[i])));
            }

            sevenSeg.SublampTable = table;
        }

        private static void ApplyDefaultSubLampTable(BaseComponent component)
        {
            if (component is not SevenSeg sevenSeg || sevenSeg.SublampTable is { Count: > 0 })
            {
                return;
            }

            // MFME always writes a SevenSeg sub-lamp table of a fixed 16 slots (undefined slots
            // are -2), so reconstruct the standard table when layout.json dropped an all-undefined
            // one.
            const int slots = 16;
            var table = new List<LampSublampTableEntry>(slots);
            for (int i = 1; i <= slots; i++)
            {
                table.Add(new LampSublampTableEntry(i, -2));
            }

            sevenSeg.SublampTable = table;
        }

        private static void ApplyImages(
            JsonObject imagesNode,
            int ordinal,
            IReadOnlyDictionary<string, byte[]> images,
            BaseComponent component)
        {
            if (imagesNode is null)
            {
                return;
            }

            foreach (KeyValuePair<string, JsonNode> kv in imagesNode)
            {
                if (kv.Value is not JsonObject meta)
                {
                    continue;
                }

                int width = ReadInt(meta["Width"]);
                int height = ReadInt(meta["Height"]);
                ushort bpp = (ushort)ReadInt(meta["BitsPerPixel"]);
                images.TryGetValue($"images/{ordinal}/{kv.Key}.bmp", out byte[] bytes);
                component.Images[kv.Key] = new BitmapEntry(width, height, bpp, bytes ?? Array.Empty<byte>())
                {
                    Purpose = kv.Key
                };
            }
        }

        private static ValueRole InferRole(JsonNode value)
        {
            if (value is not JsonValue jsonValue)
            {
                return ValueRole.RAW;
            }

            if (jsonValue.TryGetValue(out bool _)) return ValueRole.BOOLEAN;
            if (jsonValue.TryGetValue(out string s))
            {
                return s.Length == 9 && s[0] == '#' ? ValueRole.ARGB_COLOR : ValueRole.TEXT;
            }

            return ValueRole.UINT32;
        }

        private static int ReadInt(JsonNode node)
        {
            if (node is null)
            {
                return 0;
            }

            return node is JsonValue v && v.TryGetValue(out int i) ? i : (int)ReadLong(node);
        }

        private static long ReadLong(JsonNode node)
        {
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out long l)) return l;
                if (v.TryGetValue(out int i)) return i;
                if (v.TryGetValue(out uint u)) return u;
                if (v.TryGetValue(out double d)) return (long)Math.Round(d);
            }

            return 0;
        }

        private static double ReadDouble(JsonNode node)
        {
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out double d)) return d;
                if (v.TryGetValue(out long l)) return l;
                if (v.TryGetValue(out int i)) return i;
            }

            return 0.0;
        }

        private static bool ReadBool(JsonNode node)
        {
            return node is JsonValue v && v.TryGetValue(out bool b) && b;
        }

        private static string ReadEntryText(ZipArchive zip, string name)
        {
            ZipArchiveEntry entry = zip.GetEntry(name)
                ?? throw new InvalidDataException($"Export zip has no {name} member.");
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
        {
            using Stream stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
