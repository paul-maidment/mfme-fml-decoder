using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.src.Decoder.Component.Core;
using MfmeFmlDecoder.src.Model;
using MfmeFmlDecoder.Utilities;
using Xunit;

namespace MfmeFmlDecoder.UnitTests
{
    public class LayoutImporterTests
    {
        private static IEnumerable<string> FixtureFmls()
        {
            string root = Path.Combine(AppContext.BaseDirectory, "gameconfig_smoke");
            if (!Directory.Exists(root))
            {
                yield break;
            }

            foreach (string fml in Directory.EnumerateFiles(root, "layout.fml", SearchOption.AllDirectories))
            {
                yield return fml;
            }
        }

        [Fact]
        public void Import_ReSerialise_EqualsExportedLayoutJson()
        {
            var failures = new List<string>();
            int checkedCount = 0;

            foreach (string fml in FixtureFmls())
            {
                string label = Path.GetFileName(Path.GetDirectoryName(fml));
                try
                {
                    byte[] plain = FmlDecryptor.Decrypt(File.ReadAllBytes(fml));
                    var parser = new ComponentParser();
                    var walker = new FileWalker(new ComponentWalker(parser));
                    walker.WalkTlv(plain, 0);
                    Layout original = parser.ToLayout(walker.Header);

                    string tempZip = Path.Combine(Path.GetTempPath(), "imp-" + Guid.NewGuid().ToString("N") + ".zip");
                    try
                    {
                        LayoutExporter.ExportToZip(original, tempZip);
                        string expectedJson = ReadZipEntry(tempZip, "layout.json");

                        Layout imported = LayoutImporter.ImportFromZip(tempZip);
                        string actualJson = imported.ToJson(indented: true);

                        JsonNode expected = JsonNode.Parse(expectedJson);
                        JsonNode actual = JsonNode.Parse(actualJson);
                        if (!JsonNode.DeepEquals(expected, actual))
                        {
                            failures.Add($"{label}: layout.json differs after import\n" + FirstDiff(expectedJson, actualJson));
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempZip)) File.Delete(tempZip);
                    }

                    checkedCount++;
                }
                catch (Exception ex)
                {
                    failures.Add($"{label}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Assert.True(checkedCount > 0, "No gameconfig_smoke fixtures found.");
            Assert.True(failures.Count == 0, $"{failures.Count}/{checkedCount} fixtures failed:\n" + string.Join("\n", failures.Take(10)));
        }

        private static string ReadZipEntry(string zipPath, string name)
        {
            using ZipArchive zip = ZipFile.OpenRead(zipPath);
            ZipArchiveEntry entry = zip.GetEntry(name) ?? throw new FileNotFoundException(name);
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static string FirstDiff(string a, string b)
        {
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

            return $"  length differs: expected {la.Length} lines, actual {lb.Length} lines";
        }
    }
}
