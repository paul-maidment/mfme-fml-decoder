using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace MfmeFmlDecoder.SmokeTests
{
    internal static class RepoPaths
    {
        public static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MfmeFmlDecoder.csproj")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not find repository root (file MfmeFmlDecoder.csproj). Run tests from the built output of this test project.");
        }

        public static string FindDecoderDll(string repoRoot)
        {
            string configuration = GetSolutionConfiguration();
            var path = Path.Combine(repoRoot, "bin", configuration, "net6.0", "MfmeFmlDecoder.dll");
            if (File.Exists(path))
            {
                return path;
            }

            foreach (string cfg in new[] { "Debug", "Release" })
            {
                var fallback = Path.Combine(repoRoot, "bin", cfg, "net6.0", "MfmeFmlDecoder.dll");
                if (File.Exists(fallback))
                {
                    return fallback;
                }
            }

            throw new FileNotFoundException(
                $"MfmeFmlDecoder.dll not found at {path}. Build the main project first (dotnet build).");
        }

        private static string GetSolutionConfiguration()
        {
            string cfg = Environment.GetEnvironmentVariable("CONFIGURATION");
            if (!string.IsNullOrEmpty(cfg))
            {
                return cfg;
            }

            cfg = Environment.GetEnvironmentVariable("BUILD_CONFIGURATION");
            if (!string.IsNullOrEmpty(cfg))
            {
                return cfg;
            }

            return "Debug";
        }

        public static string SmokeTestRoot(string repoRoot)
        {
            return Path.Combine(repoRoot, "test", "smoke_test");
        }

        public static IEnumerable<SmokeCase> EnumerateSmokeCases(string repoRoot)
        {
            string root = SmokeTestRoot(repoRoot);
            if (!Directory.Exists(root))
            {
                yield break;
            }

            foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            {
                string layout = Path.Combine(dir, "layout.fml");
                string expected = Path.Combine(dir, "expected.json");
                if (!File.Exists(layout) || !File.Exists(expected))
                {
                    continue;
                }

                string name = Path.GetRelativePath(root, dir);
                if (Path.DirectorySeparatorChar != '/')
                {
                    name = name.Replace(Path.DirectorySeparatorChar, '/');
                }

                yield return new SmokeCase(name, layout, expected);
            }
        }
    }

    internal readonly struct SmokeCase
    {
        public SmokeCase(string name, string layoutPath, string expectedPath)
        {
            Name = name;
            LayoutPath = layoutPath;
            ExpectedPath = expectedPath;
        }

        public string Name { get; }
        public string LayoutPath { get; }
        public string ExpectedPath { get; }
    }
}