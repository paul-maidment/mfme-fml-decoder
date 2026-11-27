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

            // Allow an argument response file: pass @<path> and each line is one argument.
            if (args.Length == 1 && args[0].StartsWith("@", StringComparison.Ordinal))
            {
                args = File.ReadAllLines(args[0].Substring(1));
            }

            string inputPath = null;
            string outputPath = null;
            uint? removeTag = null;
            bool listTags = false;
            bool unban = false;
            var patches = new List<(int Offset, byte[] Bytes)>();
            var splices = new List<(int Offset, int Length, byte[] Bytes)>();

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
                else if (a is "--unban" or "-u")
                {
                    unban = true;
                }
                else if (a is "--patch" or "-p")
                {
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("Missing value for --patch");
                        return 1;
                    }

                    string spec = args[++i];
                    int colon = spec.IndexOf(':');
                    if (colon <= 0)
                    {
                        Console.Error.WriteLine("--patch wants <plaintextOffset>:<hex>");
                        return 1;
                    }

                    if (!TryParseOffset(spec.Substring(0, colon), out int poff))
                    {
                        Console.Error.WriteLine($"Invalid patch offset: {spec}");
                        return 1;
                    }

                    byte[] pbytes;
                    try
                    {
                        pbytes = Convert.FromHexString(spec.Substring(colon + 1).Replace(" ", "").Replace("-", ""));
                    }
                    catch (FormatException)
                    {
                        Console.Error.WriteLine($"Invalid patch hex: {spec}");
                        return 1;
                    }

                    patches.Add((poff, pbytes));
                }
                else if (a is "--splicefile")
                {
                    // --splicefile <plaintextOffset>:<length>:<file containing hex>
                    string spec = args[++i];
                    string[] parts = spec.Split(':') is var _ ? spec.Split(':') : null;
                    if (parts.Length < 3)
                    {
                        Console.Error.WriteLine("--splicefile wants <offset>:<length>:<hexfile>");
                        return 1;
                    }
                    if (!TryParseOffset(parts[0], out int soff2) || !TryParseOffset(parts[1], out int slen2))
                    {
                        Console.Error.WriteLine($"Invalid splice offsets: {parts[0]}:{parts[1]}");
                        return 1;
                    }
                    string hexPath = string.Join(":", parts, 2, parts.Length - 2);
                    string hexText = string.Concat(File.ReadAllLines(hexPath)).Trim();
                    splices.Add((soff2, slen2, Convert.FromHexString(hexText)));
                }
                else if (a is "--splice")
                {
                    // --splice <plaintextOffset>:<length>:<hex>  (length may change)
                    string spec = args[++i];
                    string[] parts = spec.Split('i') is var _ ? spec.Split(':') : null;
                    if (parts.Length != 3 || parts[2].Length % 2 != 0)
                    {
                        Console.Error.WriteLine("--splice wants <offset>:<length>:<hex>");
                        return 1;
                    }
                    if (!TryParseOffset(parts[0], out int soff) || !TryParseOffset(parts[1], out int slen))
                    {
                        Console.Error.WriteLine($"Invalid splice offsets: {spec}");
                        return 1;
                    }
                    splices.Add((soff, slen, Convert.FromHexString(parts[2])));
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

            if (unban && (patches.Count > 0 || removeTag != null))
            {
                Console.Error.WriteLine("--unban clears the layout blacklist; it cannot be combined with --patch or --remove-tag.");
                return 1;
            }

            if (!listTags && removeTag == null && patches.Count == 0 && splices.Count == 0 && !unban)
            {
                Console.Error.WriteLine("Specify --list, --unban, --remove-tag <id> and/or --patch <offset>:<hex>.");
                PrintUsage();
                return 1;
            }

            byte[] fml = File.ReadAllBytes(inputPath);
            byte[] plain = FmlDecryptor.Decrypt(fml);
            Console.WriteLine($"Decrypted {inputPath} ({fml.Length:N0} → {plain.Length:N0} bytes)");

            if (unban)
            {
                UnbanResult unbanResult = BlacklistUnbanner.Unban(plain);
                Console.WriteLine(
                    $"Blacklist scan: {unbanResult.PayloadsScanned} 0x{BlacklistUnbanner.LayoutPayloadTag:X2} " +
                    $"payload(s), {unbanResult.RecordsScanned} record(s)");

                foreach (string warning in unbanResult.Warnings)
                    Console.WriteLine($"  warning: {warning}");

                if (!unbanResult.Changed)
                {
                    Console.WriteLine("No blacklist marker found — file left untouched.");
                    return 0;
                }

                foreach (UnbanEdit edit in unbanResult.Edits)
                    Console.WriteLine($"  cleared {edit}");

                plain = unbanResult.Plaintext;

                if (outputPath == null)
                {
                    string backupPath = inputPath + ".bak";
                    if (File.Exists(backupPath))
                    {
                        Console.WriteLine($"Backup kept: {backupPath} already exists (not overwritten)");
                    }
                    else
                    {
                        File.Copy(inputPath, backupPath);
                        Console.WriteLine($"Backup written: {backupPath}");
                    }

                    outputPath = inputPath;
                    Console.WriteLine($"Overwriting in place: {Path.GetFullPath(outputPath)}");
                }
            }

            if (patches.Count > 0)
            {
                plain = (byte[])plain.Clone();
                foreach ((int Offset, byte[] Bytes) p in patches)
                {
                    if (p.Offset < 0 || p.Offset + p.Bytes.Length > plain.Length)
                    {
                        Console.Error.WriteLine($"Patch at 0x{p.Offset:X} ({p.Bytes.Length} B) is outside the plaintext.");
                        return 1;
                    }

                    Console.WriteLine(
                        $"Patch 0x{p.Offset:X} ({p.Bytes.Length} B): " +
                        $"{Convert.ToHexString(plain.AsSpan(p.Offset, p.Bytes.Length))} -> {Convert.ToHexString(p.Bytes)}");
                    Array.Copy(p.Bytes, 0, plain, p.Offset, p.Bytes.Length);
                }
            }

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

            byte[] edited = plain;
            if (removeTag != null)
            {
                edited = FileLevelTagBag.RemoveTag(plain, removeTag.Value, out int removed);
                if (removed == 0)
                {
                    Console.Error.WriteLine($"Tag 0x{removeTag.Value:X} not found — nothing written.");
                    return 2;
                }

                Console.WriteLine($"Removed {removed} occurrence(s) of tag 0x{removeTag.Value:X}");
                Console.WriteLine($"Plaintext {plain.Length:N0} → {edited.Length:N0} bytes");
            }

            if (splices.Count > 0)
            {
                var ordered = splices.OrderBy(s => s.Offset).ToList();
                var ms = new System.IO.MemoryStream(edited.Length + 4096);
                int cursor = 0;
                foreach ((int Offset, int Length, byte[] Bytes) s in ordered)
                {
                    if (s.Offset < cursor || s.Length < 0 || s.Offset + s.Length > edited.Length)
                    {
                        Console.Error.WriteLine($"Splice at 0x{s.Offset:X} len {s.Length} is outside the plaintext or overlaps.");
                        return 1;
                    }

                    ms.Write(edited, cursor, s.Offset - cursor);
                    Console.WriteLine($"Splice 0x{s.Offset:X} ({s.Length} -> {s.Bytes.Length} B)");
                    ms.Write(s.Bytes, 0, s.Bytes.Length);
                    cursor = s.Offset + s.Length;
                }

                ms.Write(edited, cursor, edited.Length - cursor);
                edited = ms.ToArray();
                Console.WriteLine($"Plaintext after splice: {edited.Length:N0} bytes");
            }

            byte[] repacked = FmlDecryptor.RepackAsSingleChunk(fml, edited);
            outputPath ??= Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? ".",
                Path.GetFileNameWithoutExtension(inputPath) + ".edited.fml");

            WriteAllBytesKeepingAttributes(outputPath, repacked);
            Console.WriteLine($"Wrote {outputPath} ({repacked.Length:N0} bytes)");

            // Quick sanity: decrypt again and confirm the edit survived
            byte[] verify = FmlDecryptor.Decrypt(repacked);
            if (removeTag != null)
            {
                Dictionary<uint, byte[]> after = FileLevelTagBag.Parse(verify);
                if (after.ContainsKey(removeTag.Value))
                {
                    Console.Error.WriteLine("Verify failed: tag still present after re-encrypt.");
                    return 3;
                }
            }

            if (unban)
            {
                UnbanResult recheck = BlacklistUnbanner.Unban(verify);
                if (recheck.Changed)
                {
                    Console.Error.WriteLine("Verify failed: blacklist marker still present after re-encrypt.");
                    return 3;
                }

                Console.WriteLine(
                    $"Verified: no blacklist marker in {recheck.RecordsScanned} record(s) of the rewritten file.");
            }

            foreach ((int Offset, byte[] Bytes) p in patches)
            {
                if (!verify.AsSpan(p.Offset, p.Bytes.Length).SequenceEqual(p.Bytes))
                {
                    Console.Error.WriteLine($"Verify failed: patch at 0x{p.Offset:X} did not survive re-encrypt.");
                    return 3;
                }
            }

            Console.WriteLine(
                removeTag != null ? "Verify OK (decrypt + tag absent + chunk CRC)."
                : unban ? "Verify OK (decrypt + marker cleared + chunk CRC)."
                : "Verify OK (decrypt + patch present + chunk CRC).");
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

        /// <summary>
        /// Writes <paramref name="bytes"/> to <paramref name="path"/>, temporarily
        /// clearing the read-only attribute when overwriting such a file.
        /// </summary>
        private static void WriteAllBytesKeepingAttributes(string path, byte[] bytes)
        {
            FileAttributes original = File.Exists(path) ? File.GetAttributes(path) : 0;
            bool readOnly = (original & FileAttributes.ReadOnly) != 0;
            if (readOnly)
                File.SetAttributes(path, original & ~FileAttributes.ReadOnly);

            try
            {
                File.WriteAllBytes(path, bytes);
            }
            finally
            {
                if (readOnly)
                    File.SetAttributes(path, original);
            }
        }

        private static bool TryParseOffset(string text, out int value)
        {
            text = text.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                || int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
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
                "FmlTagEdit — list, clear or remove file-level TLV tags from an FML" + Environment.NewLine +
                Environment.NewLine +
                "Usage:" + Environment.NewLine +
                "  FmlTagEdit <input.fml> --list" + Environment.NewLine +
                "  FmlTagEdit <input.fml> --unban [-o output.fml]" + Environment.NewLine +
                "  FmlTagEdit <input.fml> --remove-tag 0x0C [-o output.fml]" + Environment.NewLine +
                "  FmlTagEdit <input.fml> --patch 0xBAB:F894ECCE93BCF895FDD9 [-o out.fml]" + Environment.NewLine +
                Environment.NewLine +
                "Options:" + Environment.NewLine +
                "  --unban, -u       Clear the layout blacklist markers MFME scans for in the" + Environment.NewLine +
                "                    0x97 payload (exact match on record field 6, substrings on" + Environment.NewLine +
                "                    field 2). Without -o the file is rewritten in place, after" + Environment.NewLine +
                "                    copying it to <input>.fml.bak (an existing backup is kept)." + Environment.NewLine +
                "                    A file with no marker is left untouched." + Environment.NewLine +
                "  --patch, -p       Overwrite plaintext bytes at <plaintextOffset>:<hex>." + Environment.NewLine +
                "                    Repeatable; offsets are decrypted-plaintext offsets." + Environment.NewLine +
                "  --remove-tag, -r  Drop every file-level TLV entry with the given tag id." + Environment.NewLine +
                "  --list, -l        Print the file-level tag table." + Environment.NewLine +
                "  --output, -o      Write to this path instead of editing in place." + Environment.NewLine +
                Environment.NewLine +
                "Notes:" + Environment.NewLine +
                "  - Decrypts, edits the TLV prologue (before 0xFFFFFFFF), re-encrypts" + Environment.NewLine +
                "    with correct chunk CRC32 / AACS header (same password as input)." + Environment.NewLine +
                "  - Re-encoded output is a single chunk, so it is usually smaller." + Environment.NewLine +
                "  - Read-only inputs are overwritten and their attribute restored." + Environment.NewLine +
                "  - Default output without -o and without --unban: <input>.edited.fml");
        }
    }
}
