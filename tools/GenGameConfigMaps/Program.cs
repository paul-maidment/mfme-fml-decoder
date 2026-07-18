using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenGameConfigMaps
{
    /// <summary>
    /// Regenerates human-readable GameConfig map sources under src/GameConfig/Maps/.
    /// Input: a directory of *_GameConfig.json, or --from-cs to re-pretty existing .cs maps.
    /// </summary>
    internal static class Program
    {
        private static readonly JsonSerializerOptions PrettyJson = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static int Main(string[] args)
        {
            string repoRoot = null;
            string inputDir = null;
            bool fromCs = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--from-cs")
                {
                    fromCs = true;
                }
                else if (repoRoot == null)
                {
                    repoRoot = Path.GetFullPath(a);
                }
                else if (inputDir == null)
                {
                    inputDir = Path.GetFullPath(a);
                }
            }

            repoRoot ??= FindRepoRoot() ?? Directory.GetCurrentDirectory();
            string outDir = Path.Combine(repoRoot, "src", "GameConfig", "Maps");
            Directory.CreateDirectory(outDir);

            List<(string System, string ClassName, string PrettyJson)> maps;
            if (fromCs)
            {
                maps = LoadFromExistingCs(outDir);
                if (maps.Count == 0)
                {
                    Console.Error.WriteLine("No GameConfig map .cs files found in " + outDir);
                    return 1;
                }
            }
            else
            {
                inputDir ??= Path.Combine(repoRoot, "Maps");
                if (!Directory.Exists(inputDir))
                {
                    Console.Error.WriteLine("Input maps directory not found: " + inputDir);
                    Console.Error.WriteLine("Pass a JSON directory, or use --from-cs to rewrite existing embedded maps.");
                    return 1;
                }

                maps = LoadFromJsonDir(inputDir);
                if (maps.Count == 0)
                {
                    Console.Error.WriteLine("No *_GameConfig.json in " + inputDir);
                    return 1;
                }
            }

            var entries = new List<(string System, string ClassName)>();
            foreach ((string system, string className, string pretty) in maps.OrderBy(m => m.System, StringComparer.OrdinalIgnoreCase))
            {
                string csPath = Path.Combine(outDir, className + ".cs");
                File.WriteAllText(csPath, GenerateMapClass(system, className, pretty), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                entries.Add((system, className));
                Console.WriteLine($"Wrote {Path.GetFileName(csPath)} ({pretty.Length:N0} chars JSON)");
            }

            string regPath = Path.Combine(outDir, "GameConfigMapRegistration.cs");
            File.WriteAllText(regPath, GenerateRegistration(entries), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"Wrote {Path.GetFileName(regPath)} ({entries.Count} systems)");
            return 0;
        }

        private static List<(string System, string ClassName, string PrettyJson)> LoadFromJsonDir(string inputDir)
        {
            var maps = new List<(string, string, string)>();
            foreach (string path in Directory.GetFiles(inputDir, "*_GameConfig.json")
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileName(path);
                string systemFromFile = fileName.Substring(0, fileName.Length - "_GameConfig.json".Length);
                try
                {
                    maps.Add(ParseMapJson(File.ReadAllBytes(path), systemFromFile));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Skip {path}: {ex.Message}");
                }
            }

            return maps;
        }

        private static List<(string System, string ClassName, string PrettyJson)> LoadFromExistingCs(string mapsDir)
        {
            var maps = new List<(string, string, string)>();
            foreach (string path in Directory.GetFiles(mapsDir, "*GameConfigMap.cs")
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                if (Path.GetFileName(path).Equals("GameConfigMapRegistration.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                string text = File.ReadAllText(path);
                try
                {
                    byte[] utf8 = ExtractUtf8JsonBytes(text);
                    if (utf8 != null)
                    {
                        string fallbackSystem = Path.GetFileNameWithoutExtension(path);
                        if (fallbackSystem.EndsWith("GameConfigMap", StringComparison.Ordinal))
                            fallbackSystem = fallbackSystem.Substring(0, fallbackSystem.Length - "GameConfigMap".Length);
                        maps.Add(ParseMapJson(utf8, fallbackSystem));
                        continue;
                    }

                    string json = ExtractJsonStringLiteral(text);
                    if (json != null)
                    {
                        maps.Add(ParseMapJson(Encoding.UTF8.GetBytes(json), "unknown"));
                        continue;
                    }

                    Console.Error.WriteLine("Skip (unrecognized format): " + path);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Skip {path}: {ex.Message}");
                }
            }

            return maps;
        }

        private static (string System, string ClassName, string PrettyJson) ParseMapJson(byte[] utf8, string fallbackSystem)
        {
            using JsonDocument doc = JsonDocument.Parse(utf8);
            JsonElement root = doc.RootElement;
            string system = fallbackSystem;
            if (root.TryGetProperty("system", out JsonElement sys) &&
                sys.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(sys.GetString()))
            {
                system = sys.GetString();
            }

            // Re-serialize for stable, readable indentation.
            string pretty = JsonSerializer.Serialize(root, PrettyJson);
            // Normalize to LF for stable diffs across machines.
            pretty = pretty.Replace("\r\n", "\n").TrimEnd() + "\n";
            string className = ToClassName(system) + "GameConfigMap";
            return (system, className, pretty);
        }

        private static byte[] ExtractUtf8JsonBytes(string cs)
        {
            Match m = Regex.Match(
                cs,
                @"Utf8Json\s*=\s*\{(?<body>.*?)\}\s*;",
                RegexOptions.Singleline);
            if (!m.Success)
                return null;

            var bytes = new List<byte>();
            foreach (Match hex in Regex.Matches(m.Groups["body"].Value, @"0x([0-9A-Fa-f]{2})"))
                bytes.Add(Convert.ToByte(hex.Groups[1].Value, 16));

            return bytes.Count == 0 ? null : bytes.ToArray();
        }

        private static string ExtractJsonStringLiteral(string cs)
        {
            // const string Json = @"...";  or raw """..."""
            Match raw = Regex.Match(
                cs,
                @"Json\s*=\s*""""\s*\r?\n(?<body>.*?)""""\s*;",
                RegexOptions.Singleline);
            if (raw.Success)
                return raw.Groups["body"].Value;

            Match verbatim = Regex.Match(
                cs,
                @"Json\s*=\s*@""(?<body>(?:[^""]|"""")*)""\s*;",
                RegexOptions.Singleline);
            if (!verbatim.Success)
                return null;

            return verbatim.Groups["body"].Value.Replace("\"\"", "\"");
        }

        private static string GenerateMapClass(string system, string className, string prettyJson)
        {
            // Verbatim string: readable JSON, editable in-place ("" for quotes). LangVersion 10-safe.
            string[] lines = prettyJson
                .Replace("\r\n", "\n")
                .TrimEnd()
                .Replace("\"", "\"\"")
                .Split('\n');

            var sb = new StringBuilder();
            sb.AppendLine("// GameConfig map for " + system + ".");
            sb.AppendLine("// Editable JSON below. Optionally regenerate via tools/GenGameConfigMaps.");
            sb.AppendLine();
            sb.AppendLine("namespace MfmeFmlDecoder.GameConfig.Maps");
            sb.AppendLine("{");
            sb.AppendLine("    using MfmeFmlDecoder.GameConfig;");
            sb.AppendLine();
            sb.AppendLine("    internal static class " + className);
            sb.AppendLine("    {");
            sb.AppendLine("        public const string SystemName = " + ToCSharpString(system) + ";");
            sb.AppendLine();
            sb.AppendLine("        private const string Json =");
            sb.Append("            @\"");
            sb.Append(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                sb.Append("\r\n            ");
                sb.Append(lines[i]);
            }

            sb.AppendLine("\";");
            sb.AppendLine();
            sb.AppendLine("        public static GameConfigMap Create() =>");
            sb.AppendLine("            GameConfigMap.FromJson(SystemName, \"embedded:\" + SystemName, Json);");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string GenerateRegistration(List<(string System, string ClassName)> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Registers all GameConfig maps. Regenerated by tools/GenGameConfigMaps.");
            sb.AppendLine();
            sb.AppendLine("using MfmeFmlDecoder.GameConfig.Maps;");
            sb.AppendLine();
            sb.AppendLine("namespace MfmeFmlDecoder.GameConfig");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class GameConfigMapRegistration");
            sb.AppendLine("    {");
            sb.AppendLine("        public static void RegisterAll()");
            sb.AppendLine("        {");
            foreach ((string system, string className) in entries.OrderBy(e => e.System, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(
                    "            GameConfigMapRegistry.Register(" + className + ".SystemName, " +
                    className + ".Create);");
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ToClassName(string system)
        {
            var sb = new StringBuilder();
            bool upperNext = true;
            foreach (char c in system)
            {
                if (c == '+')
                {
                    sb.Append("Plus");
                    upperNext = true;
                    continue;
                }

                if (!char.IsLetterOrDigit(c))
                {
                    upperNext = true;
                    continue;
                }

                if (upperNext)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    upperNext = false;
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (sb.Length == 0)
                return "Unknown";
            if (char.IsDigit(sb[0]))
                sb.Insert(0, "Sys");
            return sb.ToString();
        }

        private static string ToCSharpString(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(Directory.GetCurrentDirectory());
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
