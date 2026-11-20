using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Decoder.Writer;
using MfmeFmlDecoder.GameConfig;
using MfmeFmlDecoder.src.Model;

namespace MfmeFmlDecoder.Utilities
{
    /// <summary>
    /// Turns a decoder <c>--export</c> zip into a freshly-encoded 20.1 <c>.fml</c> plus a
    /// matching <c>.gam</c>. No source file is consulted: the layout comes from
    /// <c>layout.json</c>/<c>images/</c>, the prologue from <c>machine.json</c> plus the system
    /// map, and the GAM from <c>game.json</c> plus the system map.
    /// </summary>
    internal static class Reconstructor
    {
        internal sealed class Result
        {
            public string FmlPath { get; init; }
            public string GamPath { get; init; }
            public IReadOnlyList<string> PrologueUnsupported { get; init; }
            public IReadOnlyList<string> GamUnsupported { get; init; }
        }

        public static Result Rebuild(string zipPath, string outDir)
        {
            if (string.IsNullOrWhiteSpace(zipPath)) throw new ArgumentException("Zip path required.", nameof(zipPath));
            if (!File.Exists(zipPath)) throw new FileNotFoundException("Export zip not found.", zipPath);

            string baseName = Path.GetFileNameWithoutExtension(zipPath);
            outDir ??= Path.GetDirectoryName(Path.GetFullPath(zipPath));
            Directory.CreateDirectory(outDir);

            string machineJson;
            string gameJson;
            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                machineJson = ReadRequired(zip, "machine.json");
                gameJson = ReadRequired(zip, "game.json");
            }

            string system = SystemName(machineJson) ?? SystemName(gameJson);
            if (system is null)
            {
                throw new InvalidOperationException("machine.json/game.json has no System.");
            }

            if (!GameConfigMapRegistry.TryGet(system, out GameConfigMap map, out string mapError))
            {
                throw new InvalidOperationException(mapError);
            }

            Layout layout = LayoutImporter.ImportFromZip(zipPath);
            PrologueSynthesizer.Result prologue = PrologueSynthesizer.Synthesize(
                machineJson, map, layout.Header.SplashBitmap?.Bytes);

            // The imported model carries no raw tag bytes, so presence is taken from the model:
            // the writer emits each tag the component has a value for (the export captured
            // exactly the tags that were present).
            byte[] fml = LayoutWriter.WriteFml(prologue.Prologue, layout, MfmeVersion.V20_1, emitAllModelled: true);

            string layoutFileName = baseName + ".fml";
            GamSynthesizer.Result gam = GamSynthesizer.Synthesize(gameJson, map, layoutFileName, baseName);

            string fmlPath = Path.Combine(outDir, layoutFileName);
            string gamPath = Path.Combine(outDir, baseName + ".gam");
            File.WriteAllBytes(fmlPath, fml);
            File.WriteAllText(gamPath, gam.GamText, new UTF8Encoding(false));

            return new Result
            {
                FmlPath = Path.GetFullPath(fmlPath),
                GamPath = Path.GetFullPath(gamPath),
                PrologueUnsupported = prologue.Unsupported,
                GamUnsupported = gam.Unsupported
            };
        }

        private static string ReadRequired(ZipArchive zip, string name)
        {
            ZipArchiveEntry entry = zip.GetEntry(name);
            if (entry is null)
            {
                throw new InvalidDataException($"Export zip is missing the required member '{name}'.");
            }

            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static string SystemName(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("System", out System.Text.Json.JsonElement sys) &&
                sys.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return sys.GetString();
            }

            return null;
        }
    }
}
