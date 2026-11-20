using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.Decoder.Writer;
using MfmeFmlDecoder.GameConfig;
using Xunit;

namespace MfmeFmlDecoder.UnitTests
{
    public class GamSynthesizerTests
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
                if (File.Exists(Path.Combine(dir, "expected.game.json")))
                {
                    yield return (dir, Path.GetFileName(dir));
                }
            }
        }

        [Fact]
        public void Synthesized_Gam_Decodes_To_SameGameJson()
        {
            var failures = new List<string>();
            int checkedCount = 0;

            foreach ((string dir, string system) in Fixtures())
            {
                try
                {
                    string gameJson = File.ReadAllText(Path.Combine(dir, "expected.game.json"));
                    var root = JsonNode.Parse(gameJson) as JsonObject;
                    string actualSystem = root?["System"]?.GetValue<string>() ?? system;

                    if (!GameConfigMapRegistry.TryGet(actualSystem, out GameConfigMap map, out string err))
                    {
                        failures.Add($"{system}: no map ({err})");
                        continue;
                    }

                    GamSynthesizer.Result result = GamSynthesizer.Synthesize(gameJson, map, "layout.fml", "layout");
                    if (result.Unsupported.Count > 0)
                    {
                        failures.Add($"{system}: unsupported: {string.Join(", ", result.Unsupported)}");
                        continue;
                    }

                    byte[] plain = FmlDecryptor.Decrypt(File.ReadAllBytes(Path.Combine(dir, "layout.fml")));
                    string tempGam = Path.Combine(Path.GetTempPath(), "synth-" + Guid.NewGuid().ToString("N") + ".gam");
                    try
                    {
                        File.WriteAllText(tempGam, result.GamText);
                        if (!GameConfigDecoder.TryDecode("synth", tempGam, plain, null, out GameConfigDecodeResult decoded, out string skip))
                        {
                            failures.Add($"{system}: decode failed ({skip})");
                            continue;
                        }

                        JsonNode expected = JsonNode.Parse(gameJson);
                        JsonNode actual = JsonNode.Parse(decoded.GameJson);
                        if (!JsonNode.DeepEquals(expected, actual))
                        {
                            failures.Add($"{system}: game.json differs\n" + Diff(expected, actual));
                        }

                        checkedCount++;
                    }
                    finally
                    {
                        if (File.Exists(tempGam)) File.Delete(tempGam);
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{system}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.True(checkedCount > 0 || failures.Count > 0, "No fixtures found.");
            Assert.True(failures.Count == 0, $"{failures.Count} fixtures failed:\n" + string.Join("\n", failures));
        }

        private static string Diff(JsonNode expected, JsonNode actual)
        {
            var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            string a = expected?.ToJsonString(opts) ?? "<null>";
            string b = actual?.ToJsonString(opts) ?? "<null>";
            string[] la = a.Split('\n');
            string[] lb = b.Split('\n');
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
