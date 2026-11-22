using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.GameConfig;
using MfmeFmlDecoder.src.Decoder.Component.Core;
using MfmeFmlDecoder.src.Model;
using MfmeFmlDecoder.Utilities;
using Xunit;

namespace MfmeFmlDecoder.UnitTests
{
    /// <summary>
    /// Gate A at fixture scale: export a layout to a zip, rebuild the pair from that zip,
    /// decode the rebuilt pair, and require layout.json / machine.json / game.json to match.
    /// </summary>
    public class ReconstructorTests
    {
        private static IEnumerable<(string Dir, string System)> Fixtures()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "gameconfig_smoke");
            if (!Directory.Exists(root))
            {
                yield break;
            }

            foreach (string dir in Directory.EnumerateDirectories(root))
            {
                if (File.Exists(Path.Combine(dir, "layout.fml")) && File.Exists(Path.Combine(dir, "layout.gam")))
                {
                    yield return (dir, Path.GetFileName(dir));
                }
            }
        }

        [Fact]
        public void RebuiltPair_RoundTrips_AllThreeJsonMembers()
        {
            var failures = new List<string>();
            int checkedCount = 0;

            foreach ((string dir, string system) in Fixtures())
            {
                string work = Path.Combine(Path.GetTempPath(), "rebuild-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    string fmlPath = Path.Combine(dir, "layout.fml");
                    string gamPath = Path.Combine(dir, "layout.gam");
                    byte[] plain = FmlDecryptor.Decrypt(File.ReadAllBytes(fmlPath));

                    var parser = new ComponentParser();
                    var walker = new FileWalker(new ComponentWalker(parser));
                    walker.WalkTlv(plain, 0);
                    Layout layout = parser.ToLayout(walker.Header);

                    if (!GameConfigDecoder.TryDecode(fmlPath, gamPath, plain, null, out GameConfigDecodeResult cfg, out string skip))
                    {
                        failures.Add($"{system}: fixture decode failed ({skip})");
                        continue;
                    }

                    string exportZip = Path.Combine(work, system + ".zip");
                    LayoutExporter.ExportToZip(layout, exportZip, cfg.MachineJson, cfg.GameJson);
                    string wantLayout = ReadZipEntry(exportZip, "layout.json");

                    Reconstructor.Result rebuilt = Reconstructor.Rebuild(exportZip, Path.Combine(work, "out"));
                    if (rebuilt.PrologueUnsupported.Count > 0 || rebuilt.GamUnsupported.Count > 0)
                    {
                        failures.Add($"{system}: unsupported: {string.Join(", ", rebuilt.PrologueUnsupported.Concat(rebuilt.GamUnsupported))}");
                        continue;
                    }

                    // layout.json: decode the rebuilt FML and re-serialise.
                    byte[] rebuiltPlain = FmlDecryptor.Decrypt(File.ReadAllBytes(rebuilt.FmlPath));
                    var parser2 = new ComponentParser();
                    var walker2 = new FileWalker(new ComponentWalker(parser2));
                    walker2.WalkTlv(rebuiltPlain, 0);
                    string gotLayout = InertTagDefaults.FilterJson(parser2.ToLayout(walker2.Header).ToJson(indented: true));

                    // machine.json / game.json: decode the rebuilt FML + GAM.
                    if (!GameConfigDecoder.TryDecode(rebuilt.FmlPath, rebuilt.GamPath, rebuiltPlain, null, out GameConfigDecodeResult got, out string skip2))
                    {
                        failures.Add($"{system}: rebuilt decode failed ({skip2})");
                        continue;
                    }

                    if (!JsonEquals(wantLayout, gotLayout))
                    {
                        failures.Add($"{system}: layout.json differs\n" + Diff(wantLayout, gotLayout));
                    }
                    else if (!JsonEquals(cfg.MachineJson, got.MachineJson))
                    {
                        failures.Add($"{system}: machine.json differs\n" + Diff(cfg.MachineJson, got.MachineJson));
                    }
                    else if (!JsonEquals(cfg.GameJson, got.GameJson))
                    {
                        failures.Add($"{system}: game.json differs\n" + Diff(cfg.GameJson, got.GameJson));
                    }
                    else
                    {
                        checkedCount++;
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{system}: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    try { Directory.Delete(work, recursive: true); } catch { }
                }
            }

            Assert.True(checkedCount > 0 || failures.Count > 0, "No fixtures found.");
            Assert.True(failures.Count == 0, $"{failures.Count} fixtures failed:\n" + string.Join("\n", failures));
        }

        private static string ReadZipEntry(string zipPath, string name)
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            using Stream stream = zip.GetEntry(name).Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static bool JsonEquals(string a, string b)
            => JsonNode.DeepEquals(JsonNode.Parse(a), JsonNode.Parse(b));

        private static string Diff(string a, string b)
        {
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string left = JsonNode.Parse(a).ToJsonString(opts);
            string right = JsonNode.Parse(b).ToJsonString(opts);
            string[] la = left.Split('\n');
            string[] lb = right.Split('\n');
            int n = Math.Min(la.Length, lb.Length);
            for (int i = 0; i < n; i++)
            {
                if (la[i] != lb[i])
                {
                    return $"  line {i + 1}:\n    expected: {la[i].Trim()}\n    actual:   {lb[i].Trim()}";
                }
            }

            return $"  length differs: expected {la.Length}, actual {lb.Length}";
        }
    }
}

