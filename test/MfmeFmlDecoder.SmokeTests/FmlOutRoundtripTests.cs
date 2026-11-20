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
    /// <summary>
    /// T4/T5 guard: for every smoke fixture, decode it, rebuild a fresh FML from the decoded
    /// model via <c>--fmlout</c>, then decode that output and assert it matches the fixture's
    /// <c>expected.json</c> (i.e. the encode path round-trips the model).
    /// </summary>
    public sealed class FmlOutRoundtripTests
    {
        public static IEnumerable<object[]> Cases() => SmokeDecoderTests.Cases();

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task FmlOut_output_redecodes_to_expected_json(string testName, string layoutPath, string expectedPath)
        {
            string repoRoot = RepoPaths.FindRepoRoot();
            string dllPath = RepoPaths.FindDecoderDll(repoRoot);

            string safeName = testName.Replace('/', '_').Replace('\\', '_');
            string outDir = Path.Combine(Path.GetTempPath(), "mfme-fmlout-roundtrip");
            Directory.CreateDirectory(outDir);
            string repackedPath = Path.Combine(outDir, safeName + ".repacked.fml");

            (int exit, string _, string stderr) = await RunDecoder(dllPath, layoutPath, "--fmlout=" + repackedPath);
            Assert.True(exit == 0, $"Case '{testName}': --fmlout failed (exit {exit}).{Environment.NewLine}{stderr}");
            Assert.True(File.Exists(repackedPath), $"Case '{testName}': no output written to {repackedPath}.");

            (int exit2, string stdout, string stderr2) = await RunDecoder(dllPath, repackedPath, "--json");
            Assert.True(exit2 == 0, $"Case '{testName}': re-decode failed (exit {exit2}).{Environment.NewLine}{stderr2}");

            JToken expected = JToken.Parse(File.ReadAllText(expectedPath, Encoding.UTF8));
            JToken actual = JToken.Parse(stdout);
            if (!JToken.DeepEquals(expected, actual))
            {
                string diff = JsonAssertionDiffFormatter.FormatUnifiedDiff(
                    expected.ToString(Newtonsoft.Json.Formatting.Indented),
                    actual.ToString(Newtonsoft.Json.Formatting.Indented));
                Assert.Fail($"Case '{testName}': round-tripped JSON does not match expected.{Environment.NewLine}{diff}");
            }
        }

        private static async Task<(int exit, string stdout, string stderr)> RunDecoder(
            string dllPath,
            string inputPath,
            string arg)
        {
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
            psi.ArgumentList.Add(inputPath);
            psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            Assert.NotNull(process);

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            bool finished = process.WaitForExit(300_000);
            if (!finished)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                Assert.Fail($"Decoder timed out for '{inputPath}' {arg}.");
            }

            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
    }
}
