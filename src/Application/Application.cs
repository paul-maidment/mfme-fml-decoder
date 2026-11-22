using System;
using System.Collections.Generic;
using System.IO;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.Diagnostics;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Decoder.Writer;
using MfmeFmlDecoder.GameConfig;
using MfmeFmlDecoder.src.Decoder.Component;
using MfmeFmlDecoder.Utilities;
using MfmeFmlDecoder.src.Decoder.Component.Core;

namespace MfmeFmlDecoder.Application
{
    internal sealed class Application
    {
        public Application()
        {
        }

        public int Run(string[] args)
        {
            if (LegacyPairVerifier.TryRun(args, out int verifierExitCode))
            {
                return verifierExitCode;
            }

            if (TryRunRebuild(args, out int rebuildExitCode))
            {
                return rebuildExitCode;
            }

            if (!TryParseArgs(
                    args,
                    out string fileName,
                    out uint offset,
                    out bool writeLayoutJson,
                    out bool exportLayout,
                    out string exportOutputPath,
                    out bool printMfmeVersion,
                    out bool writeFmlOut,
                    out string fmlOutPath,
                    out string parseError))
            {
                if (!string.IsNullOrEmpty(parseError))
                    RunLog.WriteErrorLine(parseError);
                PrintUsage();
                return 1;
            }

            RunLog.Quiet = writeLayoutJson || exportLayout || printMfmeVersion;
            try
            {
                string inputPath = Path.GetFullPath(fileName);
                if (!File.Exists(inputPath))
                {
                    throw new FileNotFoundException(inputPath);
                }

                if (printMfmeVersion)
                {
                    string version = ReadMfmeVersion(inputPath, offset);
                    Console.Out.WriteLine(version);
                    return 0;
                }

                if (writeFmlOut)
                {
                    return RunFmlOut(inputPath, fmlOutPath);
                }

                var componentParser = new ComponentParser();
                var fileWalker = new FileWalker(
                    new ComponentWalker(
                        componentParser
                    ));

                byte[] layoutBytes;
                if (string.Equals(Path.GetExtension(inputPath), ".fml", StringComparison.OrdinalIgnoreCase))
                {
                    layoutBytes = FmlDecryptor.Decrypt(ReadFileBytes(inputPath));
                    fileWalker.WalkTlv(layoutBytes, offset);
                }
                else
                {
                    layoutBytes = ReadFileBytes(inputPath);
                    fileWalker.WalkTlv(layoutBytes, offset);
                }

                if (writeLayoutJson || exportLayout)
                {
                    var layout = componentParser.ToLayout(fileWalker.Header);

                    if (writeLayoutJson)
                    {
                        string json = InertTagDefaults.FilterJson(layout.ToJson(indented: true));
                        Console.Out.Write(json);
                    }

                    if (exportLayout)
                    {
                        string outputZipPath = exportOutputPath ?? Path.ChangeExtension(inputPath, ".zip");
                        string machineJson = null;
                        string gameJson = null;
                        TryDecodeGameConfig(inputPath, layoutBytes, out machineJson, out gameJson);
                        LayoutExporter.ExportToZip(layout, outputZipPath, machineJson, gameJson);
                        RunLog.WriteDiagnosticLine($"Exported layout to {Path.GetFullPath(outputZipPath)}");
                    }
                }

                return 0;
            }
            catch (FileNotFoundException ex)
            {
                RunLog.WriteErrorLine($"Error: File not found - {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                RunLog.WriteErrorLine($"Error: {ex.Message}");
                return 1;
            }
            finally
            {
                RunLog.Quiet = false;
            }
        }

        private static bool TryRunRebuild(string[] args, out int exitCode)
        {
            string zip = null;
            string outDir = null;
            bool requested = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--rebuild=", StringComparison.Ordinal))
                {
                    zip = a.Substring("--rebuild=".Length).Trim();
                    requested = true;
                }
                else if (a.StartsWith("--outdir=", StringComparison.Ordinal))
                {
                    outDir = a.Substring("--outdir=".Length).Trim();
                }
            }

            if (!requested)
            {
                exitCode = 0;
                return false;
            }

            exitCode = 1;
            if (string.IsNullOrEmpty(zip))
            {
                RunLog.WriteErrorLine("Error: --rebuild requires an export zip path (--rebuild=<zip>).");
                return true;
            }

            try
            {
                Reconstructor.Result result = Reconstructor.Rebuild(zip, outDir);
                Console.Out.WriteLine($"Wrote {result.FmlPath}");
                Console.Out.WriteLine($"Wrote {result.GamPath}");
                foreach (string warning in result.PrologueUnsupported)
                {
                    RunLog.WriteDiagnosticLine("prologue: unsupported control " + warning);
                }

                foreach (string warning in result.GamUnsupported)
                {
                    RunLog.WriteDiagnosticLine("gam: unsupported control " + warning);
                }

                exitCode = 0;
            }
            catch (Exception ex)
            {
                RunLog.WriteErrorLine($"Error: {ex.Message}");
            }

            return true;
        }

        private static int RunFmlOut(string inputPath, string outputPath)
        {
            if (!string.Equals(Path.GetExtension(inputPath), ".fml", StringComparison.OrdinalIgnoreCase))
            {
                RunLog.WriteErrorLine(
                    "Error: classic .dat conversion is not implemented yet (see plan T1-T3/T5). " +
                    "--fmlout currently accepts .fml input only.");
                return 1;
            }

            byte[] plaintext = FmlDecryptor.Decrypt(ReadFileBytes(inputPath));

            var componentParser = new ComponentParser();
            var fileWalker = new FileWalker(new ComponentWalker(componentParser));

            bool previousQuiet = RunLog.Quiet;
            RunLog.Quiet = true;
            try
            {
                fileWalker.WalkTlv(plaintext, 0);
                var layout = componentParser.ToLayout(fileWalker.Header);
                // Always emit the 20.1 format. When the source is an older version, choose which
                // tags to write from the decoded values (vs their 20.1 defaults) because the old
                // source's tag ids/sections do not map onto the 20.1 tag maps.
                bool valueBasedSparse = !fileWalker.VersionParsed || fileWalker.Version != MfmeVersion.V20_1;
                if (valueBasedSparse)
                {
                    // A legacy source's raw tag ids belong to that version's tag maps. Feeding them to
                    // the 20.1 writer's raw-presence channel re-emits each id under its 20.1 meaning,
                    // which is a different field (e.g. a Lamp/SevenSeg/Button 0x08 that is not the 20.1
                    // field of that id), and the extra tags change the render. Drop the raw channel for
                    // cross-version sources so tag selection is purely value-based, exactly like the
                    // --rebuild path (which never has raw tags). Verified: --fmlout then renders 0.000%
                    // vs the original on all 15 legacy Gate B layouts (was 0.09-0.72%).
                    foreach (var comp in layout.Components)
                    {
                        comp.RawTagValues.Clear();
                        comp.NestedRawTagValues.Clear();
                    }
                }
                byte[] output = LayoutWriter.WriteFml(
                    plaintext,
                    fileWalker.PrologueEndOffset,
                    layout,
                    MfmeVersion.V20_1,
                    valueBasedSparse);

                string targetPath = outputPath ?? inputPath + ".repacked.fml";
                string fullTargetPath = Path.GetFullPath(targetPath);
                File.WriteAllBytes(fullTargetPath, output);
                Console.Out.WriteLine($"Wrote {fullTargetPath} ({output.Length} bytes)");
            }
            finally
            {
                RunLog.Quiet = previousQuiet;
            }

            return 0;
        }

        private static string ReadMfmeVersion(string inputPath, uint offset)
        {
            if (string.Equals(Path.GetExtension(inputPath), ".fml", StringComparison.OrdinalIgnoreCase))
            {
                byte[] decrypted = FmlDecryptor.Decrypt(ReadFileBytes(inputPath));
                return MfmeVersionReader.Read(decrypted, offset);
            }

            return MfmeVersionReader.Read(inputPath, offset);
        }

        private static void TryDecodeGameConfig(
            string layoutPath,
            byte[] layoutBytes,
            out string machineJson,
            out string gameJson)
        {
            machineJson = null;
            gameJson = null;

            string gamPath = SiblingGamResolver.TryResolve(layoutPath, out string skipReason);
            if (gamPath is null)
            {
                if (!string.IsNullOrEmpty(skipReason))
                    RunLog.WriteDiagnosticLine(skipReason);
                return;
            }

            if (!GameConfigDecoder.TryDecode(
                    layoutPath,
                    gamPath,
                    layoutBytes,
                    mapsDirectory: null,
                    out GameConfigDecodeResult decoded,
                    out string decodeSkip))
            {
                RunLog.WriteDiagnosticLine(
                    "GameConfig skipped: " + (decodeSkip ?? "unknown reason"));
                return;
            }

            machineJson = decoded.MachineJson;
            gameJson = decoded.GameJson;
            RunLog.WriteDiagnosticLine($"GameConfig decoded from {gamPath}");
        }

        private static bool TryParseArgs(
            string[] args,
            out string fileName,
            out uint offset,
            out bool writeLayoutJson,
            out bool exportLayout,
            out string exportOutputPath,
            out bool printMfmeVersion,
            out bool writeFmlOut,
            out string fmlOutPath,
            out string error)
        {
            fileName = null;
            offset = 0;
            writeLayoutJson = false;
            exportLayout = false;
            exportOutputPath = null;
            printMfmeVersion = false;
            writeFmlOut = false;
            fmlOutPath = null;
            error = null;

            var positionals = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--json" || string.Equals(a, "-j", StringComparison.OrdinalIgnoreCase))
                {
                    writeLayoutJson = true;
                }
                else if (a.StartsWith("--json=", StringComparison.Ordinal))
                {
                    string value = a.Substring("--json=".Length).Trim();
                    if (value.Length > 0)
                    {
                        error =
                            "Layout JSON is written to standard output only; do not pass a path in --json=. " +
                            "Use shell redirection (example: MfmeFmlDecoder file.dat --json > layout.json).";
                        return false;
                    }

                    writeLayoutJson = true;
                }
                else if (a == "--export")
                {
                    exportLayout = true;
                }
                else if (a.StartsWith("--export=", StringComparison.Ordinal))
                {
                    string value = a.Substring("--export=".Length).Trim();
                    if (value.Length == 0)
                    {
                        error = "Export output path must not be empty when using --export=.";
                        return false;
                    }

                    exportLayout = true;
                    exportOutputPath = value;
                }
                else if (a == "--mfme-version")
                {
                    printMfmeVersion = true;
                }
                else if (a == "--fmlout")
                {
                    writeFmlOut = true;
                }
                else if (a.StartsWith("--fmlout=", StringComparison.Ordinal))
                {
                    string value = a.Substring("--fmlout=".Length).Trim();
                    if (value.Length == 0)
                    {
                        error = "FML output path must not be empty when using --fmlout=.";
                        return false;
                    }

                    writeFmlOut = true;
                    fmlOutPath = value;
                }
                else if (!a.StartsWith("-", StringComparison.Ordinal))
                {
                    positionals.Add(a);
                }
                else
                {
                    error = $"Unknown option: {a}";
                    return false;
                }
            }

            if (positionals.Count == 0)
            {
                return false;
            }

            fileName = positionals[0];
            if (positionals.Count >= 2)
            {
                if (!TryParseOffset(positionals[1], out offset))
                {
                    error =
                        $"Invalid offset '{positionals[1]}'. Layout JSON is written to standard output only " +
                        "(use shell redirection to save a file).";
                    return false;
                }
            }

            if (positionals.Count > 2)
            {
                error = "Too many positional arguments (expected <file> [offset]).";
                return false;
            }

            if (printMfmeVersion && (writeLayoutJson || exportLayout))
            {
                error = "--mfme-version cannot be combined with --json or --export.";
                return false;
            }

            if (writeFmlOut && (writeLayoutJson || exportLayout || printMfmeVersion))
            {
                error = "--fmlout cannot be combined with --json, --export, or --mfme-version.";
                return false;
            }

            return true;
        }

        private static bool TryParseOffset(string s, out uint offset)
        {
            offset = 0;
            if (string.IsNullOrWhiteSpace(s))
            {
                return true;
            }

            try
            {
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    offset = Convert.ToUInt32(s.Substring(2), 16);
                }
                else
                {
                    offset = uint.Parse(s);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static byte[] ReadFileBytes(string fullInputPath)
        {
            using FileStream stream = new FileStream(
                fullInputPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 1024,
                options: FileOptions.SequentialScan);

            if (stream.Length == 0)
                return Array.Empty<byte>();

            if (stream.Length > int.MaxValue)
                throw new InvalidOperationException($"File is too large: {fullInputPath}");

            byte[] buffer = new byte[(int)stream.Length];
            int readOffset = 0;
            int remaining = buffer.Length;
            while (remaining > 0)
            {
                int read = stream.Read(buffer, readOffset, remaining);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"Unexpected EOF reading file '{fullInputPath}' (read {readOffset} of {buffer.Length} bytes).");
                }

                readOffset += read;
                remaining -= read;
            }

            return buffer;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  MfmeFmlDecoder <filename.(dat|fml)> [offset] [--json] [--export] [--fmlout]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --json, -j          Emit decoded layout as JSON on standard output only; errors go to stderr.");
            Console.WriteLine("  --export[=path]     Write layout.json and component images to a zip file.");
            Console.WriteLine("                      If a sibling .gam is found, also write machine.json (FML tags)");
            Console.WriteLine("                      and game.json (GAM settings) into the zip.");
            Console.WriteLine("                      Default output: <input-file>.zip in the same directory as the input file.");
            Console.WriteLine("  --mfme-version      Print the MFME version from the layout file (TLV tag 0x2F) on standard output.");
            Console.WriteLine("  --fmlout[=path]     Re-encode a .fml input into a fresh AACS container and write it.");
            Console.WriteLine("                      Cannot be combined with --json, --export, or --mfme-version.");
            Console.WriteLine("                      Default output: <input-file>.repacked.fml in the same directory as the input file.");
            Console.WriteLine("  --rebuild=<zip>     Rebuild a 20.1 <name>.fml + <name>.gam pair from an --export zip.");
            Console.WriteLine("                      The zip stem becomes the output base name.");
            Console.WriteLine("  --outdir=<dir>      Directory for --rebuild output (default: the zip's directory).");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  MfmeFmlDecoder file.dat --json");
            Console.WriteLine("  MfmeFmlDecoder file.dat --json > layout.json");
            Console.WriteLine("  MfmeFmlDecoder file.dat 0x1000 --json");
            Console.WriteLine("  MfmeFmlDecoder file.dat --export");
            Console.WriteLine("  MfmeFmlDecoder file.dat --export=C:\\out\\layout.zip");
            Console.WriteLine("  MfmeFmlDecoder file.fml --mfme-version");
            Console.WriteLine("  MfmeFmlDecoder file.fml --fmlout");
            Console.WriteLine("  MfmeFmlDecoder file.fml --fmlout=C:\\out\\repacked.fml");
        }

    }
}




