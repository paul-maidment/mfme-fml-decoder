using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MfmeFmlDecoder.SmokeTests
{
    public sealed class SmokeDecoderTests
    {
        public static IEnumerable<object[]> Cases()
        {
            string repoRoot = RepoPaths.FindRepoRoot();
            foreach (SmokeCase c in RepoPaths.EnumerateSmokeCases(repoRoot))
            {
                yield return new object[] { c.Name, c.LayoutPath, c.ExpectedPath };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Decode_layout_matches_expected_json(string testName, string layoutPath, string expectedPath)
        {
            string repoRoot = RepoPaths.FindRepoRoot();
            string dllPath = RepoPaths.FindDecoderDll(repoRoot);

            string expectedJson = File.ReadAllText(expectedPath, Encoding.UTF8);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            psi.ArgumentList.Add("exec");
            psi.ArgumentList.Add(dllPath);
            psi.ArgumentList.Add(layoutPath);
            psi.ArgumentList.Add("--json");

            using var process = Process.Start(psi);
            Assert.NotNull(process);

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            bool finished = process.WaitForExit(300_000);
            if (!finished)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                Assert.Fail($"Decoder timed out for case '{testName}'.");
            }

            string stdout = await stdoutTask;
            string stderr = await stderrTask;
            Assert.True(
                process.ExitCode == 0,
                $"Decoder exit code {process.ExitCode} for case '{testName}'." +
                (string.IsNullOrEmpty(stderr) ? string.Empty : Environment.NewLine + stderr));

            JToken expected = JToken.Parse(expectedJson);
            JToken actual;
            try
            {
                actual = JToken.Parse(stdout);
            }
            catch (Exception ex)
            {
                Assert.Fail($"Case '{testName}': stdout is not valid JSON.{Environment.NewLine}{stdout}{Environment.NewLine}{ex.Message}");
                return;
            }

            if (!JToken.DeepEquals(expected, actual))
            {
                string expectedIndented = expected.ToString(Newtonsoft.Json.Formatting.Indented);
                string actualIndented = actual.ToString(Newtonsoft.Json.Formatting.Indented);
                string diff = JsonAssertionDiffFormatter.FormatUnifiedDiff(expectedIndented, actualIndented);
                string msg =
                    $"Case '{testName}': JSON does not match expected.{Environment.NewLine}" +
                    diff;
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    msg += Environment.NewLine + "--- stderr ---\n" + stderr;
                }

                Assert.Fail(msg);
            }
        }

        [Fact]
        public void Repo_has_at_least_one_complete_smoke_case()
        {
            string repoRoot = RepoPaths.FindRepoRoot();
            bool any = false;
            foreach (SmokeCase _ in RepoPaths.EnumerateSmokeCases(repoRoot))
            {
                any = true;
                break;
            }

            if (!any)
            {
                string root = RepoPaths.SmokeTestRoot(repoRoot);
                Assert.Fail(
                    $"No complete smoke tests under '{root}'. Each folder in the tree must contain both layout.fml and expected.json.");
            }
        }
    }
}