using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.Decoder.Writer;
using MfmeFmlDecoder.GameConfig;
using Xunit;

namespace MfmeFmlDecoder.UnitTests
{
    public class PrologueSynthesizerTests
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
                if (File.Exists(Path.Combine(dir, "expected.machine.json")))
                {
                    yield return (dir, Path.GetFileName(dir));
                }
            }
        }

        [Fact]
        public void Synthesized_Prologue_Decodes_To_SameMachineJson()
        {
            var failures = new List<string>();
            int checkedCount = 0;

            foreach ((string dir, string system) in Fixtures())
            {
                try
                {
                    string machineJson = File.ReadAllText(Path.Combine(dir, "expected.machine.json"));
                    var root = JsonNode.Parse(machineJson) as JsonObject;
                    string actualSystem = root?["System"]?.GetValue<string>() ?? system;

                    if (!GameConfigMapRegistry.TryGet(actualSystem, out GameConfigMap map, out string err))
                    {
                        failures.Add($"{system}: no map ({err})");
                        continue;
                    }

                    PrologueSynthesizer.Result result = PrologueSynthesizer.Synthesize(machineJson, map);
                    if (result.Unsupported.Count > 0)
                    {
                        failures.Add($"{system}: unsupported controls: {string.Join(", ", result.Unsupported)}");
                        continue;
                    }

                    string gamPath = Path.Combine(dir, "layout.gam");
                    if (!GameConfigDecoder.TryDecode("synth", gamPath, result.Prologue, null, out GameConfigDecodeResult decoded, out string skip))
                    {
                        failures.Add($"{system}: decode failed ({skip})");
                        continue;
                    }

                    JsonNode expected = JsonNode.Parse(machineJson);
                    JsonNode actual = JsonNode.Parse(decoded.MachineJson);
                    if (!JsonNode.DeepEquals(expected, actual))
                    {
                        failures.Add($"{system}: machine.json differs\n" + Diff(expected, actual));
                    }

                    checkedCount++;
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
            string a = expected?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? "<null>";
            string b = actual?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? "<null>";
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
