using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.GameConfig;

namespace FmlTagEdit
{
    /// <summary>
    /// Simple FML file-level TLV editor: decrypt → list/remove tag → re-encrypt (single chunk).
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            string inputPath = null;
            string outputPath = null;
            uint? removeTag = null;
            bool listTags = false;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a is "--list" or "-l")
                {
                    listTags = true;
                }
                else if (a is "--remove-tag" or "-r")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --remove-tag");
                        return 1;
                    }

                    if (!TryParseTag(args[++i], out uint tag))
                    {
                        Console.Error.WriteLine($"Invalid tag id: {args[i]}");
                        return 1;
                    }

                    removeTag = tag;
                }
                else if (a is "--output" or "-o")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --output");
                        return 1;
                    }

                    outputPath = args[++i];
                }
                else if (a.StartsWith("-", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Unknown option: {a}");
                    PrintUsage();
                    return 1;
                }
                else if (inputPath == null)
                {
                    inputPath = a;
                }
                else
                {
                    Console.Error.WriteLine($"Unexpected argument: {a}");
                    return 1;
                }
            }

            if (string.IsNullOrWhiteSpace(inputPath))
            {
                Console.Error.WriteLine("Input .fml path required.");
                PrintUsage();
                return 1;
            }

            if (!listTags && removeTag == null)
            {
                Console.Error.WriteLine("Specify --list and/or --remove-tag <id>.");
                PrintUsage();
                return 1;
            }

            byte[] fml = File.ReadAllBytes(inputPath);
            byte[] plain = FmlDecryptor.Decrypt(fml);
            Console.WriteLine($"Decrypted {inputPath} ({fml.Length:N0} → {plain.Length:N0} bytes)");

            if (listTags)
            {
                Dictionary<uint, byte[]> tags = FileLevelTagBag.Parse(plain);
                Console.WriteLine($"File-level tags ({tags.Count}):");
                foreach (KeyValuePair<uint, byte[]> kv in tags.OrderBy(k => k.Key))
                {
                    string preview = Preview(kv.Value);
                    Console.WriteLine($"  0x{kv.Key:X4}  len={kv.Value.Length,5}  {preview}");
                }
            }

            if (removeTag == null)
                return 0;

            byte[] edited = FileLevelTagBag.RemoveTag(plain, removeTag.Value, out int removed);
            if (removed == 0)
            {
                Console.Error.WriteLine($"Tag 0x{removeTag.Value:X} not found — nothing written.");
                return 2;
            }

            Console.WriteLine($"Removed {removed} occurrence(s) of tag 0x{removeTag.Value:X}");
            Console.WriteLine($"Plaintext {plain.Length:N0} → {edited.Length:N0} bytes");

            byte[] repacked = FmlDecryptor.RepackAsSingleChunk(fml, edited);
            outputPath ??= Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".",
                Path.GetFileNameWithoutExtension(inputPath) + ".edited.fml");

            File.WriteAllBytes(outputPath, repacked);
            Console.WriteLine($"Wrote {outputPath} ({repacked.Length:N0} bytes)");

            // Quick sanity: decrypt again and confirm tag gone
            byte[] verify = FmlDecryptor.Decrypt(repacked);
            Dictionary<uint, byte[]> after = FileLevelTagBag.Parse(verify);
            if (after.ContainsKey(removeTag.Value))
            {
                Console.Error.WriteLine("Verify failed: tag still present after re-encrypt.");
                return 3;
            }

            Console.WriteLine("Verify OK (decrypt + tag absent + chunk CRC).");
            uint crc = BitConverter.ToUInt32(repacked, 136);
            // For single-chunk outputs the CRC covers the full plaintext.
            if (BitConverter.ToUInt32(repacked, 12) == 1)
            {
                uint expect = FmlDecryptor.ComputeChunkCrc32(edited);
                if (crc != expect)
                {
                    Console.Error.WriteLine($"CRC mismatch: header 0x{crc:X8} vs plaintext 0x{expect:X8}");
                    return 3;
                }
            }

            return 0;
        }

        private static bool TryParseTag(string text, out uint tag)
        {
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out tag);
            if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out tag))
                return true;
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out tag);
        }

        private static string Preview(byte[] value)
        {
            if (value == null || value.Length == 0)
                return "(empty)";

            string ascii = FileLevelTagBag.AsAsciiNullPadded(value);
            if (!string.IsNullOrEmpty(ascii) && ascii.All(c => c >= 32 && c < 127))
                return $"\"{ascii}\"";

            uint? u32 = FileLevelTagBag.AsUInt32(value);
            if (u32 != null && value.Length == 4)
                return $"u32={u32.Value} (0x{u32.Value:X})";

            int n = Math.Min(8, value.Length);
            return "hex=" + Convert.ToHexString(value.AsSpan(0, n)) + (value.Length > n ? "…" : "");
        }

        private static void PrintUsage()
        {
            Console.WriteLine(
                "FmlTagEdit — list or remove file-level TLV tags from an FML" + Environment.NewLine +
                Environment.NewLine +
                "Usage:" + Environment.NewLine +
                "  FmlTagEdit <input.fml> --list" + Environment.NewLine +
                "  FmlTagEdit <input.fml> --remove-tag 0x0C [-o output.fml]" + Environment.NewLine +
                "  FmlTagEdit <input.fml> --list --remove-tag 12 -o out.fml" + Environment.NewLine +
                Environment.NewLine +
                "Notes:" + Environment.NewLine +
                "  - Decrypts, edits the TLV prologue (before 0xFFFFFFFF), re-encrypts" + Environment.NewLine +
                "    with correct chunk CRC32 / AACS header (same password as input)." + Environment.NewLine +
                "  - Default output: <input>.edited.fml");
        }
    }
}
