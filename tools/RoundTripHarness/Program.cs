using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.GameConfig;
using MfmeFmlDecoder.src.Decoder.Component.Core;
using MfmeFmlDecoder.src.Model;
using MfmeFmlDecoder.Utilities;

namespace RoundTripHarness
{
    /// <summary>
    /// Whole-corpus Gate A: for each source layout (a .fml with sibling .gam, or a zip
    /// containing one), export it, rebuild the pair with the reconstructor, decode the rebuilt
    /// pair, and require layout.json / machine.json / game.json to match.
    ///
    /// Run it over a 20.1 corpus: the writer normalises layout representation to 20.1, so a
    /// legacy source decodes to a different (legacy) representation than the rebuilt file.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string root = @"G:\DIF_Archive_MFME_20.1";
            string outPath = null;
            int limit = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--root" && i + 1 < args.Length) root = args[++i];
                else if (args[i] == "--out" && i + 1 < args.Length) outPath = args[++i];
                else if (args[i] == "--limit" && i + 1 < args.Length) limit = int.Parse(args[++i]);
            }

            RunLog.Quiet = true;

            var items = new List<(string Fml, string Gam)>();
            foreach (string fml in Directory.EnumerateFiles(root, "*.fml", SearchOption.AllDirectories))
            {
                string gam = Path.ChangeExtension(fml, ".gam");
                items.Add((fml, File.Exists(gam) ? gam : null));
            }

            items.Sort((a, b) => string.CompareOrdinal(a.Fml, b.Fml));

            var results = new List<Result>();
            string work = Path.Combine(Path.GetTempPath(), "rth-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);

            int n = 0;
            foreach ((string fml, string gam) in items)
            {
                if (limit > 0 && n >= limit) break;
                n++;
                results.Add(Process(fml, gam, work));
                if (n % 100 == 0) Console.Error.WriteLine($"  ... {n}/{items.Count}  ok={Count(results, "ok")}");
            }

            var doc = new JsonObject
            {
                ["root"] = root,
                ["total"] = items.Count,
                ["processed"] = results.Count,
                ["ok"] = Count(results, "ok"),
                ["statuses"] = StatusCounts(results)
            };
            var arr = new JsonArray();
            foreach (Result r in results) arr.Add(new JsonObject { ["name"] = r.Name, ["status"] = r.Status, ["detail"] = r.Detail });
            doc["results"] = arr;

            string json = doc.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            });

            if (outPath != null)
            {
                File.WriteAllText(outPath, json);
                Console.Error.WriteLine("wrote " + outPath);
            }
            else
            {
                Console.Out.WriteLine(json);
            }

            try { Directory.Delete(work, recursive: true); } catch { }
            return 0;
        }

        private static Result Process(string fmlPath, string gamPath, string work)
        {
            string name = Path.GetFileName(fmlPath);
            string dir = Path.Combine(work, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                byte[] plain;
                try { plain = FmlDecryptor.Decrypt(File.ReadAllBytes(fmlPath)); }
                catch (Exception ex) { return new Result(name, "decrypt-fail", ex.Message); }

                ComponentParser parser;
                try
                {
                    parser = new ComponentParser();
                    var walker = new FileWalker(new ComponentWalker(parser));
                    walker.WalkTlv(plain, 0);
                }
                catch (Exception ex) { return new Result(name, "decode-fail", ex.Message); }

                Layout layout = parser.ToLayout();

                if (gamPath is null) return new Result(name, "no-config", "no gam");

                GameConfigDecodeResult cfg;
                string skip;
                try
                {
                    if (!GameConfigDecoder.TryDecode(fmlPath, gamPath, plain, null, out cfg, out skip))
                    {
                        return new Result(name, "no-config", skip);
                    }
                }
                catch (Exception ex) { return new Result(name, "config-fail", ex.Message); }

                string exportZip = Path.Combine(dir, "export.zip");
                LayoutExporter.ExportToZip(layout, exportZip, cfg.MachineJson, cfg.GameJson);
                string wantLayout = layout.ToJson(indented: true);

                Reconstructor.Result rebuilt;
                try { rebuilt = Reconstructor.Rebuild(exportZip, Path.Combine(dir, "out")); }
                catch (Exception ex) { return new Result(name, "rebuild-fail", ex.Message); }

                if (rebuilt.PrologueUnsupported.Count > 0 || rebuilt.GamUnsupported.Count > 0)
                {
                    string detail = "prologue:[" + string.Join(";", rebuilt.PrologueUnsupported) + "] gam:[" + string.Join(";", rebuilt.GamUnsupported) + "]";
                    return new Result(name, "unsupported", detail);
                }

                byte[] rebuiltPlain = FmlDecryptor.Decrypt(File.ReadAllBytes(rebuilt.FmlPath));
                var parser2 = new ComponentParser();
                var walker2 = new FileWalker(new ComponentWalker(parser2));
                walker2.WalkTlv(rebuiltPlain, 0);
                string gotLayout = parser2.ToLayout().ToJson(indented: true);

                if (!JsonEquals(wantLayout, gotLayout)) return new Result(name, "layout-diff", FirstDiff(wantLayout, gotLayout));

                if (!GameConfigDecoder.TryDecode(rebuilt.FmlPath, rebuilt.GamPath, rebuiltPlain, null, out GameConfigDecodeResult got, out string skip2))
                {
                    return new Result(name, "rebuilt-decode-fail", skip2);
                }

                if (!JsonEquals(cfg.MachineJson, got.MachineJson)) return new Result(name, "machine-diff", FirstDiff(cfg.MachineJson, got.MachineJson));
                if (!JsonEquals(cfg.GameJson, got.GameJson)) return new Result(name, "game-diff", FirstDiff(cfg.GameJson, got.GameJson));

                return new Result(name, "ok", "");
            }
            catch (Exception ex)
            {
                return new Result(name, "exception", ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        private static bool JsonEquals(string a, string b)
            => JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));

        private static string FirstDiff(string a, string b)
        {
            string[] la = a.Split('\n');
            string[] lb = b.Split('\n');
            int n = Math.Min(la.Length, lb.Length);
            for (int i = 0; i < n; i++)
            {
                if (la[i] != lb[i]) return $"line {i + 1}: want {la[i].Trim()} got {lb[i].Trim()}";
            }

            return $"lines {la.Length} vs {lb.Length}";
        }

        private static int Count(List<Result> r, string status)
        {
            int c = 0;
            foreach (Result x in r) if (x.Status == status) c++;
            return c;
        }

        private static JsonObject StatusCounts(List<Result> r)
        {
            var d = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Result x in r) d[x.Status] = d.TryGetValue(x.Status, out int v) ? v + 1 : 1;
            var o = new JsonObject();
            foreach (var kv in d) o[kv.Key] = kv.Value;
            return o;
        }

        private sealed record Result(string Name, string Status, string Detail);
    }
}
