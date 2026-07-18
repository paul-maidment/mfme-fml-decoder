using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.GameConfig;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MfmeFmlDecoder.UnitTests
{
    /// <summary>
    /// Golden-file GameConfig smoke: for each system under test/gameconfig_smoke/,
    /// decode the local layout.gam/layout.fml and DeepEquals machine.json + game.json.
    /// </summary>
    public class GameConfigSmokeTests
    {
        public static IEnumerable<object[]> Cases()
        {
            string root = ResolveSmokeRoot();
            if (root is null)
                yield break;

            foreach (string dir in Directory.GetDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                string source = Path.Combine(dir, "source.json");
                string machine = Path.Combine(dir, "expected.machine.json");
                string game = Path.Combine(dir, "expected.game.json");
                string gam = Path.Combine(dir, "layout.gam");
                string fml = Path.Combine(dir, "layout.fml");
                if (!File.Exists(source) || !File.Exists(machine) || !File.Exists(game) ||
                    !File.Exists(gam) || !File.Exists(fml))
                    continue;

                string name = Path.GetFileName(dir);
                yield return new object[] { name, dir };
            }
        }

        [Fact]
        public void Smoke_discovers_at_least_one_golden_case()
        {
            Assert.NotEmpty(Cases());
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Smoke_machine_and_game_match_expected_json(string system, string caseDir)
        {
            string gamPath = Path.Combine(caseDir, "layout.gam");
            string fmlPath = Path.Combine(caseDir, "layout.fml");
            Assert.True(File.Exists(gamPath), $"Missing {gamPath}");
            Assert.True(File.Exists(fmlPath), $"Missing {fmlPath}");

            // Optional: source.json may override names (defaults are layout.gam / layout.fml).
            string sourcePath = Path.Combine(caseDir, "source.json");
            if (File.Exists(sourcePath))
            {
                using JsonDocument sourceDoc = JsonDocument.Parse(File.ReadAllText(sourcePath));
                if (sourceDoc.RootElement.TryGetProperty("gam", out JsonElement gamEl))
                {
                    string rel = gamEl.GetString();
                    if (!string.IsNullOrWhiteSpace(rel) && !Path.IsPathRooted(rel) &&
                        !rel.Contains('/') && !rel.Contains('\\'))
                        gamPath = Path.Combine(caseDir, rel);
                }

                if (sourceDoc.RootElement.TryGetProperty("fml", out JsonElement fmlEl))
                {
                    string rel = fmlEl.GetString();
                    if (!string.IsNullOrWhiteSpace(rel) && !Path.IsPathRooted(rel) &&
                        !rel.Contains('/') && !rel.Contains('\\'))
                        fmlPath = Path.Combine(caseDir, rel);
                }
            }

            byte[] layoutBytes = LoadLayoutBytes(fmlPath);
            Assert.True(
                GameConfigDecoder.TryDecode(
                    fmlPath,
                    gamPath,
                    layoutBytes,
                    mapsDirectory: null,
                    out GameConfigDecodeResult result,
                    out string skip),
                $"Decode skipped for {system}: {skip}");

            AssertJsonEqual(
                system,
                "machine.json",
                File.ReadAllText(Path.Combine(caseDir, "expected.machine.json")),
                result.MachineJson);

            AssertJsonEqual(
                system,
                "game.json",
                File.ReadAllText(Path.Combine(caseDir, "expected.game.json")),
                result.GameJson);
        }

        private static void AssertJsonEqual(string system, string label, string expectedText, string actualText)
        {
            JToken expected = JToken.Parse(expectedText);
            JToken actual;
            try
            {
                actual = JToken.Parse(actualText);
            }
            catch (Exception ex)
            {
                Assert.Fail($"{system} {label}: actual is not valid JSON.\n{ex.Message}\n{actualText}");
                return;
            }

            if (JToken.DeepEquals(expected, actual))
                return;

            string expectedIndented = expected.ToString(Newtonsoft.Json.Formatting.Indented);
            string actualIndented = actual.ToString(Newtonsoft.Json.Formatting.Indented);
            string diff = FormatUnifiedDiff(expectedIndented, actualIndented);
            Assert.Fail($"{system} {label}: JSON does not match expected.\n{diff}");
        }

        private static string FormatUnifiedDiff(string expectedText, string actualText)
        {
            DiffPaneModel diff = InlineDiffBuilder.Diff(expectedText, actualText);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("--- expected");
            sb.AppendLine("+++ actual");
            int shown = 0;
            const int maxLines = 80;
            int differing = diff.Lines.Count(l => l.Type != ChangeType.Unchanged);
            foreach (DiffPiece line in diff.Lines)
            {
                if (line.Type == ChangeType.Unchanged)
                    continue;
                string prefix = line.Type switch
                {
                    ChangeType.Deleted => "-",
                    ChangeType.Inserted => "+",
                    ChangeType.Modified => "~",
                    _ => " "
                };
                sb.Append(prefix).Append(' ').AppendLine(line.Text);
                if (++shown >= maxLines)
                {
                    sb.AppendLine($"... ({differing - shown} more differing lines)");
                    break;
                }
            }

            return sb.ToString();
        }

        private static byte[] LoadLayoutBytes(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            if (string.Equals(Path.GetExtension(path), ".fml", StringComparison.OrdinalIgnoreCase))
                return FmlDecryptor.Decrypt(raw);
            return raw;
        }

        private static string ResolveSmokeRoot()
        {
            // Copied next to the test DLL when Content Include is set.
            string fromOutput = Path.Combine(AppContext.BaseDirectory, "gameconfig_smoke");
            if (Directory.Exists(fromOutput) &&
                Directory.EnumerateDirectories(fromOutput).Any())
                return fromOutput;

            // test/MfmeFmlDecoder.UnitTests/bin/Debug/net6.0 -> test/gameconfig_smoke
            string fromSource = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "gameconfig_smoke"));
            if (Directory.Exists(fromSource))
                return fromSource;

            string repo = FindRepoRoot();
            if (repo != null)
            {
                string candidate = Path.Combine(repo, "test", "gameconfig_smoke");
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MfmeFmlDecoder.csproj")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }
    }
}
