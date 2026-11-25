using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.GameConfig;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>
    /// Rebuilds the file-level FML prologue (the TLV block before the <c>0xFFFFFFFF</c>
    /// terminator) from <c>machine.json</c> plus the system map. Inverse of the machine half
    /// of <see cref="GameConfigDecoder"/>: each mapped FML control's decoded value is encoded
    /// back to its tag bytes. Tags the map does not carry are left for constants/probes.
    /// </summary>
    internal static class PrologueSynthesizer
    {
        private const uint VersionTag = 0x2F;
        private const uint TerminationTag = 0xFFFFFFFF;
        private static readonly byte[] V20_1 = { (byte)'2', (byte)'0', (byte)'.', (byte)'1' };

        internal sealed class Result
        {
            public byte[] Prologue { get; init; }
            public IReadOnlyList<string> Unsupported { get; init; }
        }

        public static Result Synthesize(string machineJson, GameConfigMap map, byte[] splashBitmap = null)
        {
            if (machineJson is null) throw new ArgumentNullException(nameof(machineJson));
            if (map is null) throw new ArgumentNullException(nameof(map));

            JsonObject root = JsonNode.Parse(machineJson) as JsonObject
                ?? throw new InvalidDataException("machine.json is not an object.");
            JsonObject settings = root["Settings"] as JsonObject ?? new JsonObject();
            JsonObject switches = settings["switches"] as JsonObject;

            var tags = new Dictionary<uint, byte[]>();
            var unsupported = new List<string>();

            // The version tag must be written at its own length: MFME 20.1 emits 0x2F len=4 "20.1".
            // FileTagLengths has 9 for 0x2F because legacy files store "MFME " as UTF-16 (9 bytes),
            // so padding "20.1" out to 9 with NULs made our prologue differ from MFME's own writer.
            // (Tested: this alone does not fix the Barcrest load crash, but it is what 20.1 writes.)
            tags[VersionTag] = V20_1;
            SeedConstants(tags);
            if (splashBitmap is { Length: > 0 })
            {
                tags[0x98] = splashBitmap;
            }

            foreach (KeyValuePair<string, JsonElement> entry in map.Controls)
            {
                string name = entry.Key;
                JsonElement control = entry.Value;
                string storage = GetString(control, "storage") ?? "fml";
                if (string.Equals(storage, "gam", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(storage, "volatile", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(storage, "unknown", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (IsReelJumperCheckboxControl(name))
                {
                    continue;
                }

                string kind = GetString(control, "kind") ?? "unknown";
                JsonNode value = switches is not null && string.Equals(kind, "switch-number", StringComparison.OrdinalIgnoreCase)
                    ? switches[name]
                    : settings[name];

                try
                {
                    EncodeControl(tags, name, kind, control, value, unsupported);
                }
                catch (Exception ex)
                {
                    unsupported.Add($"{name} ({kind}): {ex.Message}");
                }
            }

            return new Result { Prologue = Emit(tags), Unsupported = unsupported };
        }

        // Structural tags MFME needs to load, independent of any setting. Values are the corpus
        // constants (census mode across 2998 layouts). The map does not carry them, but a
        // prologue without them is rejected by MFME's loader (slice-2 probe).
        private static void SeedConstants(Dictionary<uint, byte[]> tags)
        {
            SetConstant(tags, 0x00, 0x00, 0x00, 0x00, 0x00);
            SetConstant(tags, 0x02, 0x78, 0x56, 0x34, 0x12);
            SetConstant(tags, 0x39, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
            SetConstant(tags, 0x41, 0xFF, 0x00, 0x00, 0x00);
            SetConstant(tags, 0x50, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
            SetConstant(tags, 0x51, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00);
            SetConstant(tags, 0x6E, 0x01, 0x00, 0x00, 0x00);
            SetConstant(tags, 0x6F, 0x00, 0x00, 0x00, 0x00);
        }

        private static void SetConstant(Dictionary<uint, byte[]> tags, uint tag, params byte[] value)
        {
            if (!tags.ContainsKey(tag))
            {
                tags[tag] = value;
            }
        }

        private static void EncodeControl(
            Dictionary<uint, byte[]> tags,
            string name,
            string kind,
            JsonElement control,
            JsonNode value,
            List<string> unsupported)
        {
            switch (kind)
            {
                case "checkbox":
                    EncodeCheckbox(tags, name, control, value);
                    return;
                case "text":
                    EncodeText(tags, name, control, value);
                    return;
                case "radio":
                case "dropdown":
                case "number":
                case "slider":
                case "hex-number":
                case "switch-number":
                    EncodeScalar(tags, name, kind, control, value);
                    return;
                case "effect-grid":
                    if (!IsEmptyCompound(value))
                    {
                        EncodeEffectGrid(tags, name, control, value);
                    }

                    return;
                case "meter-grid":
                    if (!IsEmptyCompound(value))
                    {
                        EncodeMeterGrid(tags, name, control, value);
                    }

                    return;
                case "record-list":
                    if (!IsEmptyCompound(value))
                    {
                        EncodeRecordList(tags, name, control, value);
                    }

                    return;
                case "indexed-enum-table":
                    if (!IsEmptyCompound(value))
                    {
                        EncodeIndexedEnumTable(tags, name, control, value);
                    }

                    return;
                case "payout-sense":
                    if (!IsEmptyCompound(value))
                    {
                        EncodePayoutSense(tags, name, control, value);
                    }

                    return;
                default:
                    if (value is null)
                    {
                        return;
                    }

                    unsupported.Add($"{name} ({kind})");
                    return;
            }
        }

        private static void EncodeCheckbox(Dictionary<uint, byte[]> tags, string name, JsonElement control, JsonNode value)
        {
            bool on = ReadBool(value);
            uint? tag = GetPrimaryTag(control);
            if (tag is null)
            {
                return;
            }

            int byteIndex = GetInt(control, "byteIndex") ?? 0;
            int bit = GetInt(control, "bit") ?? 0;
            int mask = GetInt(control, "mask") ?? (1 << bit);
            bool inverted = GetBool(control, "inverted") ?? false;

            // Tag absence always decodes to "off", even for inverted checkboxes (the inversion
            // only applies once the tag is present). So a set bit is emitted when on; an
            // inverted "on" still needs the tag present (bit clear) to decode as on.
            bool bitSet = inverted ? !on : on;
            if (bitSet)
            {
                byte[] buffer = Ensure(tags, tag.Value, byteIndex + 1);
                buffer[byteIndex] = (byte)(buffer[byteIndex] | (byte)mask);
            }
            else if (inverted)
            {
                Ensure(tags, tag.Value, byteIndex + 1);
            }
        }

        private static void EncodeText(Dictionary<uint, byte[]> tags, string name, JsonElement control, JsonNode value)
        {
            if (value is not JsonValue v || !v.TryGetValue(out string text))
            {
                return;
            }

            string encoding = GetString(control, "encoding") ?? "ascii-null-padded";
            if (string.Equals(encoding, "split-ascii", StringComparison.OrdinalIgnoreCase))
            {
                EncodeSplitAscii(tags, control, text);
                return;
            }

            uint? tag = GetPrimaryTag(control);
            if (tag is null)
            {
                return;
            }

            int size = GetInt(control, "sizeBytes") ?? FileTagLengths.ForTag(tag.Value);
            int prefix = encoding switch { "ascii-4" => 4, "ascii-8" => 8, _ => 0 };
            byte[] buffer = Ensure(tags, tag.Value, size);

            // Latin-1 (ANSI), not ASCII: decoding used to turn bytes such as 0xA3 ('£') into '?'
            // and we must write back the same bytes MFME stored.
            byte[] ascii = Encoding.Latin1.GetBytes(text ?? string.Empty);
            int max = prefix > 0 ? Math.Min(prefix, buffer.Length) : Math.Max(0, buffer.Length - 1);
            int n = Math.Min(ascii.Length, max);
            Array.Copy(ascii, 0, buffer, 0, n);
        }

        private static void EncodeSplitAscii(Dictionary<uint, byte[]> tags, JsonElement control, string text)
        {
            if (!control.TryGetProperty("parts", out JsonElement parts) || parts.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            // Latin-1 (ANSI), not ASCII: decoding used to turn bytes such as 0xA3 ('£') into '?'
            // and we must write back the same bytes MFME stored.
            byte[] ascii = Encoding.Latin1.GetBytes(text ?? string.Empty);
            int cursor = 0;
            foreach (JsonElement part in parts.EnumerateArray())
            {
                string tagText = part.TryGetProperty("tag", out JsonElement t) ? t.GetString() : null;
                if (!TryParseTag(tagText, out uint tagId))
                {
                    continue;
                }

                int offset = part.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0;
                int size = part.TryGetProperty("sizeBytes", out JsonElement sz) ? sz.GetInt32() : FileTagLengths.ForTag(tagId);
                byte[] buffer = Ensure(tags, tagId, offset + size);
                int take = Math.Min(size, Math.Max(0, ascii.Length - cursor));
                if (take > 0)
                {
                    Array.Copy(ascii, cursor, buffer, offset, take);
                    cursor += take;
                }
            }
        }

        private static void EncodeScalar(
            Dictionary<uint, byte[]> tags,
            string name,
            string kind,
            JsonElement control,
            JsonNode value)
        {
            if (value is null)
            {
                return;
            }

            uint? tag = GetPrimaryTag(control);
            if (tag is null)
            {
                return;
            }

            string encoding = GetString(control, "encoding") ?? "u32-le";
            int byteOffset = GetInt(control, "byteOffset") ?? 0;

            // Bit-field radio: value is a label whose 0/1 bit is written into a u32 word.
            if (GetInt(control, "bit") is int bit && control.TryGetProperty("values", out _))
            {
                if (!TryResolveRaw(control, value, out long bitVal))
                {
                    return;
                }

                int mask = GetInt(control, "mask") ?? (1 << bit);
                uint word = bitVal != 0 ? (uint)mask : 0u;
                byte[] buffer = Ensure(tags, tag.Value, byteOffset + 4);
                WriteU32(buffer, byteOffset, word);
                return;
            }

            if (string.Equals(encoding, "width-height-2xu32-le", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryResolveWidthHeight(control, value, out uint w, out uint h))
                {
                    return;
                }

                byte[] buffer = Ensure(tags, tag.Value, byteOffset + 8);
                WriteU32(buffer, byteOffset, w);
                WriteU32(buffer, byteOffset + 4, h);
                return;
            }

            if (!TryResolveRaw(control, value, out long raw))
            {
                return;
            }

            if (string.Equals(kind, "hex-number", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(encoding, "hex-u32-le", StringComparison.OrdinalIgnoreCase))
            {
                WriteU32(Ensure(tags, tag.Value, byteOffset + 4), byteOffset, unchecked((uint)raw));
                return;
            }

            switch (encoding)
            {
                case "bytes":
                case "u8":
                case "byte":
                    WriteByte(Ensure(tags, tag.Value, byteOffset + 1), byteOffset, (byte)raw);
                    break;
                case "u16-le":
                    WriteU16(Ensure(tags, tag.Value, byteOffset + 2), byteOffset, (ushort)raw);
                    break;
                default:
                    WriteU32(Ensure(tags, tag.Value, byteOffset + 4), byteOffset, unchecked((uint)raw));
                    break;
            }
        }

        private static bool TryResolveRaw(JsonElement control, JsonNode value, out long raw)
        {
            raw = 0;
            if (value is JsonValue v)
            {
                if (v.TryGetValue(out long l))
                {
                    raw = l;
                    return true;
                }

                if (v.TryGetValue(out string s))
                {
                    if (control.TryGetProperty("values", out JsonElement values) &&
                        values.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty p in values.EnumerateObject())
                        {
                            if (string.Equals(p.Name, s, StringComparison.Ordinal))
                            {
                                return TryLong(p.Value, out raw);
                            }
                        }
                    }

                    if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                        long.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hex))
                    {
                        raw = hex;
                        return true;
                    }

                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out raw))
                    {
                        return true;
                    }

                    // Bare-hex values (e.g. hex-number fields like "0C000") parse as hex.
                    return long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out raw);
                }
            }

            return false;
        }

        private static bool TryResolveWidthHeight(JsonElement control, JsonNode value, out uint w, out uint h)
        {
            w = 0;
            h = 0;
            string label = value is JsonValue v && v.TryGetValue(out string s) ? s : null;
            if (label is not null &&
                control.TryGetProperty("values", out JsonElement values) &&
                values.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in values.EnumerateObject())
                {
                    if (!string.Equals(p.Name, label, StringComparison.Ordinal) || p.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var arr = new List<long>();
                    foreach (JsonElement item in p.Value.EnumerateArray())
                    {
                        arr.Add(item.TryGetInt64(out long n) ? n : 0);
                    }

                    if (arr.Count >= 2)
                    {
                        w = (uint)arr[0];
                        h = (uint)arr[1];
                        return true;
                    }
                }
            }

            // Composite "W x H" / "WxH"
            if (label is not null)
            {
                string[] split = label.Split(new[] { 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);
                if (split.Length == 2 &&
                    uint.TryParse(split[0].Trim(), out w) &&
                    uint.TryParse(split[1].Trim(), out h))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EncodeEffectGrid(Dictionary<uint, byte[]> tags, string name, JsonElement control, JsonNode value)
        {
            uint? tag = GetPrimaryTag(control);
            if (tag is null || value is not JsonObject obj || obj["rows"] is not JsonArray rows)
            {
                return;
            }

            int stride = GetInt(control, "rowStride") ?? 8;
            int rowCount = GetInt(control, "rows") ?? 16;
            int onOff = 0, offOff = 4;
            if (control.TryGetProperty("fields", out JsonElement fields))
            {
                if (fields.TryGetProperty("on", out JsonElement on) && on.TryGetProperty("byteOffset", out JsonElement ob)) onOff = ob.GetInt32();
                if (fields.TryGetProperty("off", out JsonElement off) && off.TryGetProperty("byteOffset", out JsonElement fb)) offOff = fb.GetInt32();
            }

            int size = GetInt(control, "sizeBytes") ?? Math.Max(rowCount * stride, FileTagLengths.ForTag(tag.Value));
            byte[] buffer = Ensure(tags, tag.Value, size);
            Dictionary<string, long> codes = BuildReverseFromOptions(control);

            string numberKey = name.IndexOf("Triac", StringComparison.OrdinalIgnoreCase) >= 0
                ? "triac"
                : name.IndexOf("Meter", StringComparison.OrdinalIgnoreCase) >= 0 ? "meter" : "number";

            foreach (JsonNode rowNode in rows)
            {
                if (rowNode is not JsonObject row)
                {
                    continue;
                }

                int index = (int)ReadLong(row[numberKey]) - 1;
                if (index < 0)
                {
                    continue;
                }

                int baseOffset = index * stride;
                if (baseOffset + Math.Max(onOff, offOff) + 4 > buffer.Length)
                {
                    continue;
                }

                WriteU32(buffer, baseOffset + onOff, (uint)EffectCode(codes, row["on"]));
                WriteU32(buffer, baseOffset + offOff, (uint)EffectCode(codes, row["off"]));
            }
        }

        private static long EffectCode(Dictionary<string, long> codes, JsonNode label)
        {
            if (label is not JsonValue v || !v.TryGetValue(out string s) || string.IsNullOrEmpty(s))
            {
                return 0;
            }

            if (codes.TryGetValue(s, out long code))
            {
                return code;
            }

            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : 0;
        }

        private static void EncodeMeterGrid(Dictionary<uint, byte[]> tags, string name, JsonElement control, JsonNode value)
        {
            uint? tag = GetPrimaryTag(control);
            if (tag is null || value is not JsonObject obj)
            {
                return;
            }

            int size = GetInt(control, "sizeBytes") ?? FileTagLengths.ForTag(tag.Value);
            byte[] buffer = Ensure(tags, tag.Value, size);

            (int baseOff, int rowCount, int stride, int meterIn, int meterOut, int triacIn, int triacOut) mt = (0, 16, 16, 0, 4, 8, 12);
            if (control.TryGetProperty("meterTriacBlock", out JsonElement mtb))
            {
                mt.baseOff = mtb.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 0;
                mt.rowCount = mtb.TryGetProperty("rowCount", out JsonElement rc) ? rc.GetInt32() : 16;
                mt.stride = mtb.TryGetProperty("rowStride", out JsonElement rs) ? rs.GetInt32() : 16;
                if (mtb.TryGetProperty("rowLayout", out JsonElement rl))
                {
                    mt.meterIn = rl.TryGetProperty("meterIn", out JsonElement a) ? a.GetInt32() : 0;
                    mt.meterOut = rl.TryGetProperty("meterOut", out JsonElement b) ? b.GetInt32() : 4;
                    mt.triacIn = rl.TryGetProperty("triacIn", out JsonElement c) ? c.GetInt32() : 8;
                    mt.triacOut = rl.TryGetProperty("triacOut", out JsonElement d) ? d.GetInt32() : 12;
                }
            }

            (int baseOff, int count, int stride, int inOff, int outOff) sec = (256, 32, 8, 0, 4);
            if (control.TryGetProperty("secBlock", out JsonElement sb))
            {
                sec.baseOff = sb.TryGetProperty("byteOffset", out JsonElement bo) ? bo.GetInt32() : 256;
                sec.count = sb.TryGetProperty("entryCount", out JsonElement ec) ? ec.GetInt32() : 32;
                sec.stride = sb.TryGetProperty("entryStride", out JsonElement es) ? es.GetInt32() : 8;
                if (sb.TryGetProperty("entryLayout", out JsonElement el))
                {
                    sec.inOff = el.TryGetProperty("in", out JsonElement a) ? a.GetInt32() : 0;
                    sec.outOff = el.TryGetProperty("out", out JsonElement b) ? b.GetInt32() : 4;
                }
            }

            Dictionary<string, long> inMult = ReverseValues(control, "inMultiplierValues");
            Dictionary<string, long> outMult = ReverseValues(control, "outMultiplierValues");

            WriteChannels(obj["in"] as JsonArray, inMult, isIn: true);
            WriteChannels(obj["out"] as JsonArray, outMult, isIn: false);

            void WriteChannels(JsonArray channels, Dictionary<string, long> mult, bool isIn)
            {
                if (channels is null)
                {
                    return;
                }

                foreach (JsonNode entryNode in channels)
                {
                    if (entryNode is not JsonObject entry)
                    {
                        continue;
                    }

                    string source = entry["source"] is JsonValue sv && sv.TryGetValue(out string s) ? s : null;
                    if (!TryParseChannel(source, out string kind, out int n))
                    {
                        continue;
                    }

                    int offset;
                    if (kind == "Sec")
                    {
                        offset = sec.baseOff + (n - 1) * sec.stride + (isIn ? sec.inOff : sec.outOff);
                    }
                    else
                    {
                        int row = mt.baseOff + (n - 1) * mt.stride;
                        offset = kind == "Meter" ? row + (isIn ? mt.meterIn : mt.meterOut) : row + (isIn ? mt.triacIn : mt.triacOut);
                    }

                    if (offset < 0 || offset + 4 > buffer.Length)
                    {
                        continue;
                    }

                    long code = 0;
                    if (entry["multiplier"] is JsonValue mv && mv.TryGetValue(out string label) && !string.IsNullOrEmpty(label))
                    {
                        code = mult.TryGetValue(label, out long c)
                            ? c
                            : (long.TryParse(label, NumberStyles.Integer, CultureInfo.InvariantCulture, out long numeric) ? numeric : 0);
                    }

                    WriteU32(buffer, offset, unchecked((uint)code));
                }
            }
        }

        private static bool TryParseChannel(string source, out string kind, out int n)
        {
            kind = null;
            n = 0;
            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            string[] parts = source.Split(' ');
            if (parts.Length != 2 || !int.TryParse(parts[1], out n))
            {
                return false;
            }

            kind = parts[0];
            return kind is "Meter" or "Triac" or "Sec";
        }

        private static void EncodeRecordList(Dictionary<uint, byte[]> tags, string name, JsonElement control, JsonNode value)
        {
            if (value is not JsonObject obj || obj["records"] is not JsonArray records || records.Count == 0)
            {
                return;
            }

            if (!control.TryGetProperty("recordTags", out JsonElement recordTags) || recordTags.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            Dictionary<string, long> outputTypes = ReverseValues(control, "outputTypeValues");

            var body = (tag: 0u, stride: 0, fields: (Dictionary<string, int>)null, ok: false);
            var label = (tag: 0u, size: 0, ok: false);
            var shortcut = (tag: 0u, ok: false);

            foreach (JsonProperty rt in recordTags.EnumerateObject())
            {
                if (!TryParseTag(rt.Name, out uint tagId))
                {
                    continue;
                }

                string encoding = rt.Value.TryGetProperty("encoding", out JsonElement enc) ? enc.GetString() : null;
                int size = rt.Value.TryGetProperty("sizeBytes", out JsonElement sz) ? sz.GetInt32() : 0;

                if (string.Equals(encoding, "4xu32-le", StringComparison.OrdinalIgnoreCase))
                {
                    var fields = new Dictionary<string, int>(StringComparer.Ordinal);
                    if (rt.Value.TryGetProperty("fields", out JsonElement f))
                    {
                        foreach (JsonProperty fp in f.EnumerateObject())
                        {
                            fields[fp.Name] = fp.Value.GetInt32();
                        }
                    }

                    body = (tagId, size > 0 ? size : 16, fields, true);
                }
                else if (string.Equals(encoding, "ascii-null-padded", StringComparison.OrdinalIgnoreCase))
                {
                    label = (tagId, size > 0 ? size : 21, true);
                }
                else if (rt.Value.TryGetProperty("field", out JsonElement field) && field.GetString() == "shortcut")
                {
                    shortcut = (tagId, true);
                }
            }

            // Per-tag entry counts: the source may have different counts per tag (e.g. body 27,
            // label/shortcut 28) and the decoder derives the record count from the longest tag.
            // A record only gets a body/label/shortcut entry when it actually has those fields.
            int bodyCount = 0, labelCount = 0, shortcutCount = 0;
            foreach (JsonNode node in records)
            {
                if (node is not JsonObject rec) continue;
                int idx = rec["index"] is JsonValue iv && iv.TryGetValue(out int e) ? e : 0;
                if (rec.ContainsKey("output") || rec.ContainsKey("outputType") || rec.ContainsKey("value1") || rec.ContainsKey("value2"))
                    bodyCount = Math.Max(bodyCount, idx + 1);
                if (rec.ContainsKey("label"))
                    labelCount = Math.Max(labelCount, idx + 1);
                if (rec.ContainsKey("shortcut"))
                    shortcutCount = Math.Max(shortcutCount, idx + 1);
            }

            byte[] bodyBuf = body.ok && bodyCount > 0 ? AssignExact(tags, body.tag, bodyCount * body.stride) : null;
            byte[] labelBuf = label.ok && labelCount > 0 ? AssignExact(tags, label.tag, labelCount * label.size) : null;
            byte[] shortcutBuf = shortcut.ok && shortcutCount > 0 ? AssignExact(tags, shortcut.tag, shortcutCount * 4) : null;

            foreach (JsonNode node in records)
            {
                if (node is not JsonObject rec) continue;
                int i = rec["index"] is JsonValue iv2 && iv2.TryGetValue(out int ei) ? ei : 0;

                if (bodyBuf is not null && body.fields is not null
                    && (rec.ContainsKey("output") || rec.ContainsKey("outputType") || rec.ContainsKey("value1") || rec.ContainsKey("value2")))
                {
                    int recBase = i * body.stride;
                    if (recBase + body.stride <= bodyBuf.Length)
                    {
                        foreach (KeyValuePair<string, int> f in body.fields)
                        {
                            if (recBase + f.Value + 4 > bodyBuf.Length) continue;
                            long raw = 0;
                            if (f.Key == "outputType")
                            {
                                if (rec[f.Key] is JsonValue otv && otv.TryGetValue(out string ot))
                                    raw = outputTypes.TryGetValue(ot, out long c) ? c : 0;
                            }
                            else if (f.Key is "value1" or "value2")
                            {
                                raw = rec[f.Key] is null ? 0 : ReadLong(rec[f.Key]) + 1;
                            }
                            else
                            {
                                raw = ReadLong(rec[f.Key]);
                            }
                            WriteU32(bodyBuf, recBase + f.Value, unchecked((uint)raw));
                        }
                    }
                }

                if (labelBuf is not null && rec.ContainsKey("label"))
                {
                    int recBase = i * label.size;
                    string text = rec["label"] is JsonValue lv && lv.TryGetValue(out string ls) ? ls : string.Empty;
                    // Latin-1 (ANSI), not ASCII: decoding used to turn bytes such as 0xA3 ('£') into '?'
            // and we must write back the same bytes MFME stored.
            byte[] ascii = Encoding.Latin1.GetBytes(text ?? string.Empty);
                    int n = Math.Min(ascii.Length, Math.Max(0, label.size - 1));
                    if (recBase + label.size <= labelBuf.Length && recBase + n <= labelBuf.Length)
                        Array.Copy(ascii, 0, labelBuf, recBase, n);
                }

                if (shortcutBuf is not null && rec["shortcut"] is not null)
                {
                    int recBase = i * 4;
                    if (recBase + 4 <= shortcutBuf.Length)
                    {
                        shortcutBuf[recBase] = (byte)ReadLong(rec["shortcut"]);
                        if (rec["shortcutMods"] is not null)
                            shortcutBuf[recBase + 1] = (byte)ReadLong(rec["shortcutMods"]);
                    }
                }
            }
        }

        private static byte[] AssignExact(Dictionary<uint, byte[]> tags, uint tag, int length)
        {
            var buffer = new byte[length];
            tags[tag] = buffer;
            return buffer;
        }

        private static void EncodeIndexedEnumTable(Dictionary<uint, byte[]> tags, string name, JsonElement control, JsonNode value)
        {
            uint? tag = GetPrimaryTag(control);
            if (tag is null || value is not JsonObject obj || obj["entries"] is not JsonArray entries || entries.Count == 0)
            {
                return;
            }

            string entryEncoding = GetString(control, "entryEncoding") ?? "u16-le";
            bool wide = entryEncoding.Contains("u16", StringComparison.Ordinal) == false;
            int entrySize = wide ? 4 : 2;
            byte[] buffer = Ensure(tags, tag.Value, entries.Count * entrySize);

            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is not JsonObject entry)
                {
                    continue;
                }

                uint raw = unchecked((uint)ReadLong(entry["raw"]));
                if (wide)
                {
                    WriteU32(buffer, i * entrySize, raw);
                }
                else
                {
                    WriteU16(buffer, i * entrySize, (ushort)raw);
                }
            }
        }

        private static void EncodePayoutSense(Dictionary<uint, byte[]> tags, string name, JsonElement control, JsonNode value)
        {
            if (value is not JsonObject obj)
            {
                return;
            }

            EncodeRows("button", "Button", obj);
            EncodeRows("mask", "Mask", obj);

            void EncodeRows(string controlKey, string valueKey, JsonObject machineObj)
            {
                if (!control.TryGetProperty(controlKey, out JsonElement cells) || cells.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                JsonObject row = machineObj[valueKey] as JsonObject;
                foreach (JsonProperty cell in cells.EnumerateObject())
                {
                    JsonNode cellValue = row?[cell.Name];
                    if (cellValue is null)
                    {
                        continue;
                    }

                    uint? tag = GetPrimaryTag(cell.Value);
                    if (tag is null)
                    {
                        continue;
                    }

                    string encoding = GetString(cell.Value, "encoding") ?? "u32-le";
                    int byteOffset = GetInt(cell.Value, "byteOffset") ?? 0;
                    long raw;
                    if (string.Equals(encoding, "hex-u32-le", StringComparison.OrdinalIgnoreCase))
                    {
                        if (cellValue is not JsonValue hv || !hv.TryGetValue(out string hs) ||
                            !long.TryParse(hs, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out raw))
                        {
                            continue;
                        }
                    }
                    else
                    {
                        raw = ReadLong(cellValue);
                    }

                    if (string.Equals(encoding, "bytes", StringComparison.OrdinalIgnoreCase))
                    {
                        // A "bytes" cell is ONE byte at byteOffset inside a fixed-length tag. Sizing it
                        // byteOffset+4 and writing a u32 made the tag a byte too long (MPU3 tag 0x10
                        // came out as 5 bytes "00 13 00 00 00" instead of 4 "00 13 00 00") and MFME
                        // access-violated (004F691B, read of 0x000000D8) loading those layouts.
                        byte[] cellBytes = Ensure(tags, tag.Value, byteOffset + 1);
                        cellBytes[byteOffset] = unchecked((byte)raw);
                    }
                    else
                    {
                        WriteU32(Ensure(tags, tag.Value, byteOffset + 4), byteOffset, unchecked((uint)raw));
                    }
                }
            }
        }

        private static Dictionary<string, long> ReverseValues(JsonElement control, string propName)
        {
            var map = new Dictionary<string, long>(StringComparer.Ordinal);
            if (control.TryGetProperty(propName, out JsonElement values) && values.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in values.EnumerateObject())
                {
                    if (p.Value.TryGetInt64(out long n) && !map.ContainsKey(p.Name))
                    {
                        map[p.Name] = n;
                    }
                }
            }

            return map;
        }

        private static Dictionary<string, long> BuildReverseFromOptions(JsonElement control)
        {
            var map = new Dictionary<string, long>(StringComparer.Ordinal);
            if (control.TryGetProperty("confirmedEffectCodes", out JsonElement codes) && codes.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in codes.EnumerateObject())
                {
                    if (p.Value.TryGetInt64(out long n))
                    {
                        map[p.Name] = n;
                    }
                }
            }

            if (control.TryGetProperty("options", out JsonElement options) && options.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (JsonElement opt in options.EnumerateArray())
                {
                    string label = opt.GetString() ?? string.Empty;
                    if (!map.ContainsKey(label))
                    {
                        map[label] = i;
                    }

                    i++;
                }
            }

            return map;
        }

        private static bool IsEmptyCompound(JsonNode value)
        {
            if (value is not JsonObject obj)
            {
                return value is null;
            }

            foreach (KeyValuePair<string, JsonNode> kv in obj)
            {
                if (kv.Value is JsonArray arr && arr.Count == 0)
                {
                    continue;
                }

                if (kv.Value is JsonObject b && b.Count == 0)
                {
                    continue;
                }

                if (kv.Value is not null)
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] Ensure(Dictionary<uint, byte[]> tags, uint tag, int minLength)
        {
            int length = Math.Max(FileTagLengths.ForTag(tag), minLength);
            if (!tags.TryGetValue(tag, out byte[] buffer) || buffer.Length < length)
            {
                byte[] grown = new byte[length];
                if (buffer is not null)
                {
                    Array.Copy(buffer, grown, buffer.Length);
                }

                tags[tag] = grown;
                buffer = grown;
            }

            return buffer;
        }

        private static void SetBytes(
            Dictionary<uint, byte[]> tags,
            uint tag,
            byte[] fallback,
            int minLength,
            byte[] source,
            int sourceOffset,
            int count)
        {
            byte[] buffer = Ensure(tags, tag, minLength);
            int n = Math.Min(count, buffer.Length);
            if (source is not null && n > 0)
            {
                Array.Copy(source, sourceOffset, buffer, 0, n);
            }
        }

        private static byte[] Emit(Dictionary<uint, byte[]> tags)
        {
            var keys = new List<uint>(tags.Keys);
            keys.Sort();

            using var ms = new System.IO.MemoryStream();
            Span<byte> header = stackalloc byte[8];
            foreach (uint tag in keys)
            {
                byte[] payload = tags[tag];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header, tag);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4), (uint)payload.Length);
                ms.Write(header);
                ms.Write(payload, 0, payload.Length);
            }

            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header, TerminationTag);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4), 0);
            ms.Write(header);
            return ms.ToArray();
        }

        private static void WriteByte(byte[] buffer, int offset, byte value) => buffer[offset] = value;

        private static void WriteU16(byte[] buffer, int offset, ushort value)
            => System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset, 2), value);

        private static void WriteU32(byte[] buffer, int offset, uint value)
            => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), value);

        private static bool IsReelJumperCheckboxControl(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("Reel Jumpers ", StringComparison.Ordinal))
            {
                return false;
            }

            int j = name.LastIndexOf(" J", StringComparison.Ordinal);
            return j >= 0 && j + 3 == name.Length && name[j + 2] >= '1' && name[j + 2] <= '8';
        }

        private static uint? GetPrimaryTag(JsonElement control)
        {
            if (control.TryGetProperty("tag", out JsonElement tagEl) &&
                TryParseTag(tagEl.GetString(), out uint t))
            {
                return t;
            }

            if (control.TryGetProperty("tags", out JsonElement tags) && tags.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement el in tags.EnumerateArray())
                {
                    if (TryParseTag(el.GetString(), out uint t2))
                    {
                        return t2;
                    }
                }
            }

            return null;
        }

        private static bool TryParseTag(string text, out uint tag)
        {
            tag = 0;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out tag);
            }

            return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out tag);
        }

        private static bool TryLong(JsonElement el, out long value)
        {
            value = 0;
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out value))
            {
                return true;
            }

            return el.ValueKind == JsonValueKind.String &&
                   long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool ReadBool(JsonNode value)
        {
            return value is JsonValue v && v.TryGetValue(out bool b) && b;
        }

        private static long ReadLong(JsonNode node)
        {
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out long l)) return l;
                if (v.TryGetValue(out int i)) return i;
                if (v.TryGetValue(out uint u)) return u;
            }

            return 0;
        }

        private static string GetString(JsonElement el, string name) =>
            el.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        private static int? GetInt(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out JsonElement p))
            {
                return null;
            }

            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int i))
            {
                return i;
            }

            if (p.ValueKind == JsonValueKind.String &&
                int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
            {
                return i;
            }

            return null;
        }

        private static bool? GetBool(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out JsonElement p))
            {
                return null;
            }

            if (p.ValueKind == JsonValueKind.True) return true;
            if (p.ValueKind == JsonValueKind.False) return false;
            return null;
        }
    }
}
