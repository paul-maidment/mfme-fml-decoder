using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.GameConfig;

namespace CoverageCensus
{
    /// <summary>
    /// Read-only corpus census: what file-level FML tags and GAM keys actually appear
    /// in the legacy corpus, and which of them the GameConfig maps cover.
    /// Emits a JSON report plus a ranked text summary.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string root = @"G:\DifLegacySection(originalZips)";
            string outPath = null;
            int limit = 0;
            string dump = null;
            string dumpImport = null;
            string dumpComp = null;
            string dumpCompIndex = null;
            string dumpPayload = null;
            string dumpPayloadIndex = null;
            string dumpDecoded = null;
            string dumpRaw = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--root" && i + 1 < args.Length) root = args[++i];
                else if (args[i] == "--out" && i + 1 < args.Length) outPath = args[++i];
                else if (args[i] == "--limit" && i + 1 < args.Length) limit = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (args[i] == "--dump" && i + 1 < args.Length) dump = args[++i];
                else if (args[i] == "--dumpimport" && i + 1 < args.Length) dumpImport = args[++i];
                else if (args[i] == "--dumpcomp" && i + 2 < args.Length) { dumpComp = args[++i]; dumpCompIndex = args[++i]; }
                else if (args[i] == "--dumppayload" && i + 2 < args.Length) { dumpPayload = args[++i]; dumpPayloadIndex = args[++i]; }
                else if (args[i] == "--dumpdecoded" && i + 1 < args.Length) dumpDecoded = args[++i];
                else if (args[i] == "--dumprraw" && i + 1 < args.Length) dumpRaw = args[++i];
            }


            MfmeFmlDecoder.Utilities.RunLog.Quiet = true;

            if (dump != null)
            {
                DumpFile(dump);
                return 0;
            }

            if (dumpImport != null)
            {
                DumpImport(dumpImport);
                return 0;
            }

            if (dumpComp != null)
            {
                DumpComponent(dumpComp, int.Parse(dumpCompIndex ?? "0", CultureInfo.InvariantCulture));
                return 0;
            }

            if (dumpPayload != null && dumpPayloadIndex != null)
            {
                DumpPayload(dumpPayload, int.Parse(dumpPayloadIndex, CultureInfo.InvariantCulture));
                return 0;
            }

            if (dumpDecoded != null)
            {
                DumpDecoded(dumpDecoded);
                return 0;
            }

            if (dumpRaw != null)
            {
                DumpRaw(dumpRaw);
                return 0;
            }


            if (!Directory.Exists(root))
            {
                Console.Error.WriteLine("Root not found: " + root);
                return 1;
            }

            MfmeFmlDecoder.Utilities.RunLog.Quiet = true;


            var zips = new List<string>(Directory.EnumerateFiles(root, "*.zip", SearchOption.AllDirectories));
            zips.Sort(StringComparer.OrdinalIgnoreCase);
            Console.Error.WriteLine($"zips: {zips.Count}");

            var tagStats = new Dictionary<uint, TagStat>();
            var gamStats = new Dictionary<string, GamStat>(StringComparer.OrdinalIgnoreCase);
            var systemStats = new Dictionary<string, SystemStat>(StringComparer.OrdinalIgnoreCase);
            var mapErrors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int processed = 0, failed = 0, noMap = 0;
            int n = 0;
            foreach (string zip in zips)
            {
                if (limit > 0 && n >= limit) break;
                n++;
                try
                {
                    if (ProcessZip(zip, tagStats, gamStats, systemStats, mapErrors, out bool mapMissing))
                    {
                        processed++;
                        if (mapMissing) noMap++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"FAIL {Path.GetFileName(zip)}: {ex.Message}");
                }

                if ((processed + failed) % 250 == 0)
                    Console.Error.WriteLine($"  ... {processed + failed}/{zips.Count} ({failed} failed)");
            }

            string json = BuildJson(processed, failed, noMap, zips.Count, tagStats, gamStats, systemStats, mapErrors);
            if (outPath != null)
            {
                File.WriteAllText(outPath, json, new UTF8Encoding(false));
                Console.Error.WriteLine("wrote " + outPath);
            }
            else
            {
                Console.Out.WriteLine(json);
            }

            PrintSummary(tagStats, gamStats);
            return 0;
        }

        private static bool ProcessZip(
            string zip,
            Dictionary<uint, TagStat> tagStats,
            Dictionary<string, GamStat> gamStats,
            Dictionary<string, SystemStat> systemStats,
            Dictionary<string, int> mapErrors,
            out bool mapMissing)
        {
            mapMissing = false;
            byte[] fml = null;
            string gamText = null;
            using (var za = ZipFile.OpenRead(zip))
            {
                ZipArchiveEntry fmlEntry = null, gamEntry = null;
                foreach (ZipArchiveEntry e in za.Entries)
                {
                    string name = e.Name;
                    if (name.EndsWith(".fml", StringComparison.OrdinalIgnoreCase))
                    {
                        fmlEntry ??= e;
                        if (gamEntry != null && BaseName(e) == BaseName(gamEntry)) fmlEntry = e;
                    }
                    else if (name.EndsWith(".gam", StringComparison.OrdinalIgnoreCase))
                    {
                        gamEntry ??= e;
                        if (fmlEntry != null && BaseName(e) == BaseName(fmlEntry)) gamEntry = e;
                    }
                }

                if (fmlEntry == null) return false;
                fml = ReadAll(fmlEntry);
                if (gamEntry != null)
                {
                    using var s = gamEntry.Open();
                    using var r = new StreamReader(s, Encoding.UTF8);
                    gamText = r.ReadToEnd();
                }
            }

            byte[] plain = FmlDecryptor.Decrypt(fml);
            Dictionary<uint, byte[]> tags = FileLevelTagBag.Parse(plain);

            string system = null;
            GameConfigMap map = null;
            if (gamText != null)
            {
                GamFile gam = GamFile.Parse(zip, gamText);
                system = gam.System;
            }

            string mapKey = system ?? "(no-system)";
            if (system != null && GameConfigMapRegistry.TryGet(system, out map, out string mapErr))
            {
                // ok
            }
            else
            {
                mapMissing = true;
                if (system == null) mapErrors["(no-gam)"] = mapErrors.GetValueOrDefault("(no-gam)") + 1;
                else mapErrors[system] = mapErrors.GetValueOrDefault(system) + 1;
            }

            if (!systemStats.TryGetValue(mapKey, out SystemStat ss))
            {
                ss = new SystemStat { System = mapKey };
                systemStats[mapKey] = ss;
            }
            ss.Layouts++;
            if (map == null) ss.NoMap++;

            var coveredTags = new HashSet<uint>();
            var coveredGamKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (map != null)
            {
                foreach (var kv in map.Controls)
                {
                    CollectTags(kv.Value, coveredTags);
                    CollectGamKeys(kv.Value, coveredGamKeys);
                }
            }

            foreach (var kv in tags)
            {
                if (!tagStats.TryGetValue(kv.Key, out TagStat ts))
                {
                    ts = new TagStat { Tag = kv.Key };
                    tagStats[kv.Key] = ts;
                }
                ts.Present++;
                bool covered = coveredTags.Contains(kv.Key);
                if (covered) ts.Covered++;
                ts.Systems[mapKey] = ts.Systems.GetValueOrDefault(mapKey) + 1;
                RecordValue(ts, kv.Value);
                if (!covered)
                {
                    ts.Uncovered++;
                    if (ts.ExampleHex == null) ts.ExampleHex = TruncHex(kv.Value, 24);
                    if (ts.ExampleLen == 0) ts.ExampleLen = kv.Value?.Length ?? 0;
                    ss.UncoveredTags.Add(kv.Key);
                }
            }

            if (gamText != null)
            {
                GamFile gam = GamFile.Parse(zip, gamText);
                foreach (var key in gam.Fields.Keys)
                {
                    if (!gamStats.TryGetValue(key, out GamStat gs))
                    {
                        gs = new GamStat { Key = key };
                        gamStats[key] = gs;
                    }
                    gs.Present++;
                    bool covered = coveredGamKeys.Contains(key);
                    if (covered) gs.Covered++;
                    gs.Systems[mapKey] = gs.Systems.GetValueOrDefault(mapKey) + 1;
                    if (gam.Fields.TryGetValue(key, out string gv))
                        gs.Values[gv] = gs.Values.GetValueOrDefault(gv) + 1;
                    if (!covered)
                    {
                        gs.Uncovered++;
                        ss.UncoveredGamKeys.Add(key);
                    }
                }
            }

            return true;
        }

        private static string BaseName(ZipArchiveEntry e)
        {
            string n = e.Name;
            int dot = n.LastIndexOf('.');
            return dot < 0 ? n : n.Substring(0, dot);
        }

        private static byte[] ReadAll(ZipArchiveEntry e)
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }

        private static void DumpFile(string path)
        {
            byte[] fml;
            if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var za = ZipFile.OpenRead(path);
                ZipArchiveEntry e = null;
                foreach (ZipArchiveEntry en in za.Entries)
                    if (en.Name.EndsWith(".fml", StringComparison.OrdinalIgnoreCase)) { e = en; break; }
                fml = ReadAll(e);
            }
            else
            {
                fml = File.ReadAllBytes(path);
            }

            byte[] plain = FmlDecryptor.Decrypt(fml);
            Console.Out.WriteLine($"decrypted plaintext: {plain.Length} bytes");
            int pos = 0;
            int idx = 0;
            while (pos + 8 <= plain.Length)
            {
                uint tag = BitConverter.ToUInt32(plain, pos);
                uint len = BitConverter.ToUInt32(plain, pos + 4);
                int valPos = pos + 8;
                if (tag == 0xFFFFFFFF)
                {
                    Console.Out.WriteLine($"[{idx,3}] @0x{pos:X6} TERMINATOR   len=0x{len:X}");
                    pos = valPos;
                    break;
                }
                if (len > plain.Length - valPos)
                {
                    Console.Out.WriteLine($"[{idx,3}] @0x{pos:X6} tag=0x{tag:X2} len=0x{len:X} OVERRUN");
                    break;
                }

                int n = (int)Math.Min(len, 48);
                string hex = n > 0 ? Convert.ToHexString(plain, valPos, n) : "";
                string ascii = "";
                for (int i = 0; i < n; i++)
                {
                    byte b = plain[valPos + i];
                    ascii += b >= 0x20 && b < 0x7F ? (char)b : '.';
                }
                Console.Out.WriteLine($"[{idx,3}] @0x{pos:X6} tag=0x{tag:X2} len={len,-7} {hex,-96} {ascii}");
                pos = valPos + (int)len;
                idx++;
            }

            Console.Out.WriteLine($"first component candidate at 0x{pos:X6}: tag=0x{BitConverter.ToUInt32(plain, pos):X2} len=0x{BitConverter.ToUInt32(plain, pos + 4):X}");
        }

        private static void DumpRaw(string fmlPath)
        {
            byte[] plain = MfmeFmlDecoder.Decryption.FmlDecryptor.Decrypt(File.ReadAllBytes(fmlPath));
            var parser = new MfmeFmlDecoder.src.Decoder.Component.Core.ComponentParser();
            var walker = new MfmeFmlDecoder.Decoder.FileWalker(new MfmeFmlDecoder.Decoder.ComponentWalker(parser));
            walker.WalkTlv(plain, 0);
            var layout = parser.ToLayout(walker.Header);
            int i = 0;
            foreach (var c in layout.Components)
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var kv in c.RawTagValues) parts.Add($"R{kv.Key:X2}={Convert.ToHexString(kv.Value)}");
                foreach (var kv in c.NestedRawTagValues) parts.Add($"N{kv.Key:X2}={Convert.ToHexString(kv.Value)}");
                if (parts.Count > 0) Console.Out.WriteLine($"{i} {c.GetType().Name} {string.Join(" ", parts)}");
                i++;
            }
        }

        private static void DumpDecoded(string fmlPath)
        {
            byte[] plain = MfmeFmlDecoder.Decryption.FmlDecryptor.Decrypt(File.ReadAllBytes(fmlPath));
            var parser = new MfmeFmlDecoder.src.Decoder.Component.Core.ComponentParser();
            var walker = new MfmeFmlDecoder.Decoder.FileWalker(new MfmeFmlDecoder.Decoder.ComponentWalker(parser));
            walker.WalkTlv(plain, 0);
            var layout = parser.ToLayout(walker.Header);
            int i = 0;
            foreach (var c in layout.Components)
            {
                var prop = c.GetType().GetProperty("SublampTable");
                if (prop != null && prop.GetValue(c) is System.Collections.IEnumerable e)
                {
                    var vals = new System.Collections.Generic.List<string>();
                    foreach (var o in e) vals.Add(o.ToString());
                    if (vals.Count > 0 || c.GetType().Name == "Reel")
                    {
                        Console.Out.WriteLine($"{i} {c.GetType().Name} SublampTable=[{string.Join(",", vals)}] u32=[{string.Join(",", c.UInt32s.Keys)}]");
                    }
                }
                i++;
            }
        }

        private static void DumpPayload(string zipPath, int index)
        {
            MfmeFmlDecoder.src.Model.Layout layout = MfmeFmlDecoder.Utilities.LayoutImporter.ImportFromZip(zipPath);
            MfmeFmlDecoder.src.Model.Component.BaseComponent c = layout.Components[index];
            var specials = MfmeFmlDecoder.Decoder.Writer.ComponentSpecialValues.Create(c);
            var pprop = c.GetType().GetProperty("SublampTable");
            int propCount = -1;
            if (pprop?.GetValue(c) is System.Collections.IEnumerable pe) { propCount = 0; foreach (var _ in pe) propCount++; }
            Console.Out.WriteLine($"{c.GetType().Name} propSublamp={propCount} specialsSublamp={(specials.SublampTable?.Count.ToString() ?? "null")} uint32=[{string.Join(",", c.UInt32s.Keys)}]");
            byte[] payload = MfmeFmlDecoder.Decoder.Writer.ComponentPayloadWriter.WritePayload(
                c, MfmeFmlDecoder.Decoder.MfmeVersion.V20_1, false, emitAllModelled: true);
            Console.Out.WriteLine($"{c.GetType().Name} payload {payload.Length} bytes");
            Console.Out.WriteLine(Convert.ToHexString(payload));
        }

        private static void DumpComponent(string fmlPath, int index)
        {
            byte[] plain = MfmeFmlDecoder.Decryption.FmlDecryptor.Decrypt(File.ReadAllBytes(fmlPath));
            var parser = new MfmeFmlDecoder.src.Decoder.Component.Core.ComponentParser();
            var walker = new MfmeFmlDecoder.Decoder.FileWalker(new MfmeFmlDecoder.Decoder.ComponentWalker(parser));
            walker.WalkTlv(plain, 0);
            int pos = walker.PrologueEndOffset;

            int idx = 0;
            while (pos + 8 <= plain.Length)
            {
                uint kind = BitConverter.ToUInt32(plain, pos);
                uint size = BitConverter.ToUInt32(plain, pos + 4);
                if (size < 8 || pos + size > plain.Length) break;
                if (idx == index)
                {
                    int n = (int)(size - 8);
                    Console.Out.WriteLine($"component {idx} kind=0x{kind:X2} size={size} payload={n}");
                    Console.Out.WriteLine(Convert.ToHexString(plain, pos + 8, Math.Min(n, 3000)));
                    return;
                }

                pos += (int)size;
                idx++;
            }

            Console.Out.WriteLine("component not found");
        }

        private static void DumpImport(string zipPath)
        {
            MfmeFmlDecoder.src.Model.Layout layout = MfmeFmlDecoder.Utilities.LayoutImporter.ImportFromZip(zipPath);
            foreach (MfmeFmlDecoder.src.Model.Component.BaseComponent c in layout.Components)
            {
                string sub = "";
                var prop = c.GetType().GetProperty("SublampTable");
                if (prop != null && prop.GetValue(c) is System.Collections.IEnumerable e)
                {
                    int n = 0;
                    foreach (object _ in e) n++;
                    sub = " SublampTable.count=" + n;
                }

                string extra = c.UInt32s.TryGetValue("NonNullSublampCount", out uint c1) ? " count(Reel)=" + c1 : "";
                extra += c.UInt32s.TryGetValue("NumberOfDefinedLampNumbers", out uint c2) ? " count(Lamp)=" + c2 : "";
                Console.Out.WriteLine($"{c.GetType().Name}{sub}{extra} keys={string.Join(",", c.UInt32s.Keys)} imgs={c.Images.Count}");
            }
        }

        private static string TruncHex(byte[] v, int max)
        {
            if (v == null) return null;
            int n = Math.Min(v.Length, max);
            return Convert.ToHexString(v, 0, n);
        }

        private static void RecordValue(TagStat ts, byte[] v)
        {
            if (v == null) return;
            if (v.Length <= 16)
                ts.Values[Convert.ToHexString(v)] = ts.Values.GetValueOrDefault(Convert.ToHexString(v)) + 1;
            else
                ts.Large = true;
        }

        private static void CollectTags(JsonElement el, HashSet<uint> set)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty p in el.EnumerateObject())
                    {
                        if (p.NameEquals("tag")) TryAddTagValue(p.Value, set);
                        else if (p.NameEquals("tags"))
                        {
                            if (p.Value.ValueKind == JsonValueKind.Array)
                                foreach (JsonElement item in p.Value.EnumerateArray()) TryAddTagValue(item, set);
                        }
                        else if (p.NameEquals("recordTags") && p.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty rt in p.Value.EnumerateObject()) TryAddTagName(rt.Name, set);
                        }
                        CollectTags(p.Value, set);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in el.EnumerateArray()) CollectTags(item, set);
                    break;
            }
        }

        private static void TryAddTagValue(JsonElement el, HashSet<uint> set)
        {
            if (el.ValueKind == JsonValueKind.String) TryAddTagName(el.GetString(), set);
        }

        private static void TryAddTagName(string text, HashSet<uint> set)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            uint tag;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out tag);
            else
                uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out tag);
            if (tag != 0) set.Add(tag);
        }

        private static void CollectGamKeys(JsonElement el, HashSet<string> set)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (JsonProperty p in el.EnumerateObject())
                    {
                        if (p.NameEquals("gamLine") && p.Value.ValueKind == JsonValueKind.String)
                            AddGamKey(p.Value.GetString(), set);
                        else if (p.NameEquals("gamLines") && p.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement item in p.Value.EnumerateArray())
                                if (item.ValueKind == JsonValueKind.String) AddGamKey(item.GetString(), set);
                        }
                        else if (p.NameEquals("banks") && p.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (JsonProperty bank in p.Value.EnumerateObject())
                            {
                                if (bank.Value.ValueKind == JsonValueKind.Object &&
                                    bank.Value.TryGetProperty("gamLine", out JsonElement gl) &&
                                    gl.ValueKind == JsonValueKind.String)
                                    AddGamKey(gl.GetString(), set);
                            }
                        }
                        CollectGamKeys(p.Value, set);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (JsonElement item in el.EnumerateArray()) CollectGamKeys(item, set);
                    break;
            }
        }

        private static void AddGamKey(string gamLine, HashSet<string> set)
        {
            if (string.IsNullOrWhiteSpace(gamLine)) return;
            int sp = gamLine.IndexOf(' ');
            set.Add(sp < 0 ? gamLine.Trim() : gamLine.Substring(0, sp).Trim());
        }

        private static string BuildJson(
            int processed, int failed, int noMap, int total,
            Dictionary<uint, TagStat> tagStats,
            Dictionary<string, GamStat> gamStats,
            Dictionary<string, SystemStat> systemStats,
            Dictionary<string, int> mapErrors)
        {
            var tagArr = new JsonArray();
            foreach (var kv in SortedTags(tagStats))
            {
                TagStat t = kv.Value;
                var vals = new JsonObject();
                foreach (var vv in SortedStringInt(t.Values)) vals[vv.Key] = vv.Value;
                tagArr.Add(new JsonObject
                {
                    ["tag"] = $"0x{t.Tag:X2}",
                    ["present"] = t.Present,
                    ["covered"] = t.Covered,
                    ["uncovered"] = t.Uncovered,
                    ["exampleLen"] = t.ExampleLen,
                    ["exampleHex"] = t.ExampleHex,
                    ["largePayload"] = t.Large,
                    ["distinctSmall"] = t.Values.Count,
                    ["values"] = vals,
                    ["systems"] = ToJson(t.Systems)
                });
            }

            var gamArr = new JsonArray();
            foreach (var kv in SortedGam(gamStats))
            {
                GamStat g = kv.Value;
                var gvals = new JsonObject();
                int shown = 0;
                foreach (var vv in SortedStringInt(g.Values))
                {
                    if (shown++ >= 8) break;
                    gvals[vv.Key] = vv.Value;
                }
                gamArr.Add(new JsonObject
                {
                    ["key"] = g.Key,
                    ["present"] = g.Present,
                    ["covered"] = g.Covered,
                    ["uncovered"] = g.Uncovered,
                    ["distinct"] = g.Values.Count,
                    ["values"] = gvals,
                    ["systems"] = ToJson(g.Systems)
                });
            }

            var sysArr = new JsonArray();
            foreach (var kv in SortedSystem(systemStats))
            {
                SystemStat s = kv.Value;
                var un = new JsonArray();
                foreach (uint t in SortedUints(s.UncoveredTags)) un.Add($"0x{t:X2}");
                var ug = new JsonArray();
                foreach (string k in SortedStrings(s.UncoveredGamKeys)) ug.Add(k);
                sysArr.Add(new JsonObject
                {
                    ["system"] = s.System,
                    ["layouts"] = s.Layouts,
                    ["noMap"] = s.NoMap,
                    ["uncoveredTags"] = un,
                    ["uncoveredGamKeys"] = ug
                });
            }

            var me = new JsonObject();
            foreach (var kv in mapErrors) me[kv.Key] = kv.Value;

            var doc = new JsonObject
            {
                ["corpusZips"] = total,
                ["processed"] = processed,
                ["failed"] = failed,
                ["noMap"] = noMap,
                ["mapErrors"] = me,
                ["tagCoverage"] = tagArr,
                ["gamKeyCoverage"] = gamArr,
                ["systems"] = sysArr
            };
            return doc.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            });
        }

        private static JsonObject ToJson(Dictionary<string, int> d)
        {
            var o = new JsonObject();
            foreach (var kv in SortedStringInt(d)) o[kv.Key] = kv.Value;
            return o;
        }

        private static List<KeyValuePair<uint, TagStat>> SortedTags(Dictionary<uint, TagStat> d)
        {
            var l = new List<KeyValuePair<uint, TagStat>>(d);
            l.Sort((a, b) => a.Key.CompareTo(b.Key));
            return l;
        }

        private static List<KeyValuePair<string, GamStat>> SortedGam(Dictionary<string, GamStat> d)
        {
            var l = new List<KeyValuePair<string, GamStat>>(d);
            l.Sort((a, b) => b.Value.Present.CompareTo(a.Value.Present));
            return l;
        }

        private static List<KeyValuePair<string, SystemStat>> SortedSystem(Dictionary<string, SystemStat> d)
        {
            var l = new List<KeyValuePair<string, SystemStat>>(d);
            l.Sort((a, b) => b.Value.Layouts.CompareTo(a.Value.Layouts));
            return l;
        }

        private static List<uint> SortedUints(HashSet<uint> s)
        {
            var l = new List<uint>(s);
            l.Sort();
            return l;
        }

        private static List<string> SortedStrings(HashSet<string> s)
        {
            var l = new List<string>(s);
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        private static List<KeyValuePair<string, int>> SortedStringInt(Dictionary<string, int> d)
        {
            var l = new List<KeyValuePair<string, int>>(d);
            l.Sort((a, b) => b.Value.CompareTo(a.Value));
            return l;
        }

        private static void PrintSummary(Dictionary<uint, TagStat> tagStats, Dictionary<string, GamStat> gamStats)
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine("=== file-level FML tags: present but NOT covered by map (ranked) ===");
            var tags = new List<TagStat>(tagStats.Values);
            tags.Sort((a, b) => b.Uncovered.CompareTo(a.Uncovered));
            Console.Out.WriteLine($"{"tag",-6} {"present",8} {"cov",5} {"uncov",6} {"len",6}  distinct  example");
            foreach (TagStat t in tags)
            {
                if (t.Uncovered == 0) continue;
                string distinct = t.Large ? "many(large)" : t.Values.Count.ToString(CultureInfo.InvariantCulture);
                Console.Out.WriteLine($"0x{t.Tag,-4:X2} {t.Present,8} {t.Covered,5} {t.Uncovered,6} {t.ExampleLen,6}  {distinct,-10}  {t.ExampleHex}");
            }

            Console.Out.WriteLine();
            Console.Out.WriteLine("=== GAM keys: present but NOT covered by map (ranked) ===");
            var gs = new List<GamStat>(gamStats.Values);
            gs.Sort((a, b) => b.Uncovered.CompareTo(a.Uncovered));
            Console.Out.WriteLine($"{"key",-22} {"present",8} {"cov",5} {"uncov",6} {"distinct",8}  example");
            foreach (GamStat g in gs)
            {
                if (g.Uncovered == 0) continue;
                string ex = g.Values.Count > 0 ? SortedStringInt(g.Values)[0].Key : "";
                Console.Out.WriteLine($"{g.Key,-22} {g.Present,8} {g.Covered,5} {g.Uncovered,6} {g.Values.Count,8}  {ex}");
            }
        }

        private sealed class TagStat
        {
            public uint Tag;
            public int Present;
            public int Covered;
            public int Uncovered;
            public int ExampleLen;
            public string ExampleHex;
            public bool Large;
            public Dictionary<string, int> Values = new(StringComparer.Ordinal);
            public Dictionary<string, int> Systems = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class GamStat
        {
            public string Key;
            public int Present;
            public int Covered;
            public int Uncovered;
            public Dictionary<string, int> Values = new(StringComparer.Ordinal);
            public Dictionary<string, int> Systems = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class SystemStat
        {
            public string System;
            public int Layouts;
            public int NoMap;
            public HashSet<uint> UncoveredTags = new();
            public HashSet<string> UncoveredGamKeys = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}

