using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.GameConfig;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>
    /// Rebuilds the GAM text from <c>game.json</c> plus the system map. Inverse of the GAM half
    /// of <see cref="GameConfigDecoder"/>: each decoded game control is written back as its
    /// <c>gamLine</c> text (with bitmask lines re-accumulated from their checkbox bits).
    /// Controls the source omitted (marked <c>defaultUsed</c>) are left out.
    /// </summary>
    internal static class GamSynthesizer
    {
        internal sealed class Result
        {
            public string GamText { get; init; }
            public IReadOnlyList<string> Unsupported { get; init; }
        }

        public static Result Synthesize(string gameJson, GameConfigMap map, string layoutFileName, string gameName)
        {
            if (gameJson is null) throw new ArgumentNullException(nameof(gameJson));
            if (map is null) throw new ArgumentNullException(nameof(map));

            JsonObject root = JsonNode.Parse(gameJson) as JsonObject
                ?? throw new InvalidDataException("game.json is not an object.");
            JsonObject settings = root["Settings"] as JsonObject ?? new JsonObject();
            string system = root["System"]?.GetValue<string>() ?? map.System;

            var lines = new List<string> { "System " + system };
            var unsupported = new List<string>();
            var bitmaskWords = new Dictionary<string, uint>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, JsonElement> entry in map.Controls)
            {
                string name = entry.Key;
                JsonElement control = entry.Value;
                if (!string.Equals(GetString(control, "storage"), "gam", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string kind = GetString(control, "kind") ?? "unknown";
                JsonNode value = settings[name];

                try
                {
                    EncodeControl(lines, bitmaskWords, name, kind, control, value, unsupported);
                }
                catch (Exception ex)
                {
                    unsupported.Add($"{name} ({kind}): {ex.Message}");
                }
            }

            foreach (KeyValuePair<string, uint> word in bitmaskWords.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                lines.Add($"{word.Key} {word.Value}");
            }

            if (!string.IsNullOrEmpty(layoutFileName))
            {
                lines.Add("Layout " + layoutFileName);
            }

            if (!string.IsNullOrEmpty(gameName))
            {
                lines.Add("Game " + gameName);
            }

            var text = new StringBuilder();
            foreach (string line in lines)
            {
                text.Append(line).Append('\n');
            }

            return new Result { GamText = text.ToString(), Unsupported = unsupported };
        }

        private static void EncodeControl(
            List<string> lines,
            Dictionary<string, uint> bitmaskWords,
            string name,
            string kind,
            JsonElement control,
            JsonNode value,
            List<string> unsupported)
        {
            if (value is not JsonObject decoded)
            {
                return; // absent control -> decoder re-derives the default
            }

            if (GetBool(decoded, "defaultUsed") == true)
            {
                return;
            }

            if (string.Equals(kind, "dip-banks", StringComparison.OrdinalIgnoreCase))
            {
                EncodeDipBanks(lines, control, decoded);
                return;
            }

            string gamLine = GetString(control, "gamLine");
            if (string.IsNullOrEmpty(gamLine))
            {
                if (control.TryGetProperty("gamLines", out JsonElement gl) && gl.ValueKind == JsonValueKind.Array && gl.GetArrayLength() > 0)
                {
                    gamLine = gl[0].GetString();
                }
            }

            if (string.IsNullOrEmpty(gamLine))
            {
                unsupported.Add($"{name} ({kind})");
                return;
            }

            string key = GamLineKey(gamLine);

            // Companion lines (e.g. SetPercent alongside Percentage) live under "also".
            if (decoded["also"] is JsonObject also)
            {
                foreach (KeyValuePair<string, JsonNode> extra in also)
                {
                    lines.Add($"{RestoreKey(extra.Key)} {NodeToString(extra.Value)}");
                }
            }

            if (gamLine.Contains("<bitmask>", StringComparison.OrdinalIgnoreCase))
            {
                bool on = GetBool(decoded, "value") ?? false;
                int mask = GetInt(control, "mask") ?? 0;

                // The decoded raw is the whole line word (it can carry bits no control maps, so
                // reproducing it needs the raw, not just the OR of the mask bits).
                uint raw = decoded["raw"] is not null ? unchecked((uint)ReadLong(decoded["raw"])) : 0u;
                if (raw == 0 && on)
                {
                    raw = (uint)mask;
                }

                bitmaskWords[key] = raw;
                return;
            }

            if (gamLine.Contains("1 when checked", StringComparison.OrdinalIgnoreCase))
            {
                string raw = decoded["raw"]?.GetValue<string>();
                lines.Add($"{key} {raw ?? "0"}");
                return;
            }

            if (gamLine.Contains("<0|1>", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "checkbox", StringComparison.OrdinalIgnoreCase))
            {
                bool on = GetBool(decoded, "value") ?? false;
                lines.Add($"{key} {(on ? "1" : "0")}");
                return;
            }

            if (gamLine.Contains("<index>", StringComparison.OrdinalIgnoreCase))
            {
                long idx = decoded["raw"] is not null ? ReadLong(decoded["raw"]) : ResolveIndex(control, decoded["value"]);
                lines.Add($"{key} {idx}");
                return;
            }

            JsonNode v = decoded["value"];
            lines.Add($"{key} {NodeToString(v)}");
        }

        private static void EncodeDipBanks(List<string> lines, JsonElement control, JsonObject decoded)
        {
            if (decoded["banks"] is not JsonObject banks)
            {
                return;
            }

            foreach (KeyValuePair<string, JsonNode> bank in banks)
            {
                if (bank.Value is not JsonObject b || GetBool(b, "defaultUsed") == true)
                {
                    continue;
                }

                string pattern = b["pattern"]?.GetValue<string>() ?? string.Empty;
                string gamLine = bank.Key;
                if (control.TryGetProperty("banks", out JsonElement bankDefs) &&
                    bankDefs.ValueKind == JsonValueKind.Object &&
                    bankDefs.TryGetProperty(bank.Key, out JsonElement def) &&
                    def.TryGetProperty("gamLine", out JsonElement gl) &&
                    gl.ValueKind == JsonValueKind.String)
                {
                    gamLine = gl.GetString();
                }

                lines.Add($"{gamLine} {pattern}");
            }
        }

        private static long ResolveIndex(JsonElement control, JsonNode value)
        {
            if (value is JsonValue v && v.TryGetValue(out long l))
            {
                return l;
            }

            if (value is JsonValue sv && sv.TryGetValue(out string label) &&
                control.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in values.EnumerateObject())
                {
                    if (string.Equals(p.Name, label, StringComparison.Ordinal) && p.Value.TryGetInt64(out long n))
                    {
                        return n;
                    }
                }
            }

            return 0;
        }

        private static string RestoreKey(string camel)
        {
            if (string.IsNullOrEmpty(camel))
            {
                return camel;
            }

            // "setPercent" -> "SetPercent" (companion keys are camelised on the way out)
            return char.ToUpperInvariant(camel[0]) + camel.Substring(1);
        }

        private static string GamLineKey(string gamLine)
        {
            int space = gamLine.IndexOf(' ');
            return space < 0 ? gamLine.Trim() : gamLine.Substring(0, space).Trim();
        }

        private static string NodeToString(JsonNode node)
        {
            if (node is null) return string.Empty;
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out string s)) return s;
                if (v.TryGetValue(out long l)) return l.ToString(CultureInfo.InvariantCulture);
            }

            return node.ToJsonString();
        }

        private static long ReadLong(JsonNode node)
        {
            if (node is JsonValue v)
            {
                if (v.TryGetValue(out long l)) return l;
                if (v.TryGetValue(out int i)) return i;
                if (v.TryGetValue(out string s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long p)) return p;
            }

            return 0;
        }

        private static string GetString(JsonElement el, string name) =>
            el.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

        private static int? GetInt(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out JsonElement p)) return null;
            if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int i)) return i;
            if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i;
            return null;
        }

        private static bool? GetBool(JsonElement el, string name)
        {
            if (!el.TryGetProperty(name, out JsonElement p)) return null;
            if (p.ValueKind == JsonValueKind.True) return true;
            if (p.ValueKind == JsonValueKind.False) return false;
            return null;
        }

        private static bool? GetBool(JsonObject obj, string name)
        {
            if (obj?[name] is JsonValue v && v.TryGetValue(out bool b)) return b;
            return null;
        }
    }
}
