using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Decoder.Writer;
using MfmeFmlDecoder.GameConfig;
using MfmeFmlDecoder.src.Decoder.Component.Core;
using MfmeFmlDecoder.src.Model;

namespace PrologueProbe
{
    /// <summary>
    /// Minimal-prologue probe. Keeps the source component stream intact and rewrites only the
    /// file-level prologue, writing several candidate prologues so MFME can be asked which
    /// tags it actually needs to load and render.
    /// </summary>
    internal static class Program
    {
        private sealed record Span(uint Tag, int Start, int End, int ValueLength);

        private static int Main(string[] args)
        {
            string fmlPath = null, gamPath = null, outDir = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--fml" && i + 1 < args.Length) fmlPath = args[++i];
                else if (args[i] == "--gam" && i + 1 < args.Length) gamPath = args[++i];
                else if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
            }

            if (fmlPath is null || outDir is null)
            {
                Console.Error.WriteLine("usage: PrologueProbe --fml <fml> [--gam <gam>] --out <dir>");
                return 1;
            }

            Directory.CreateDirectory(outDir);
            byte[] plain = FmlDecryptor.Decrypt(File.ReadAllBytes(fmlPath));
            (List<Span> spans, int prologueEnd) = WalkPrologue(plain);
            byte[] components = plain[prologueEnd..];

            var variants = new Dictionary<string, HashSet<uint>>(StringComparer.Ordinal)
            {
                ["v_all"] = new HashSet<uint>(spans.Select(s => s.Tag)),
                ["v_version"] = new HashSet<uint> { 0x2F },
                ["v_const"] = new HashSet<uint> { 0x2F, 0x00, 0x02, 0x39, 0x41, 0x50, 0x51, 0x6E, 0x6F },
            };
            variants["v_const_text"] = new HashSet<uint>(variants["v_const"]) { 0x0C, 0x2C, 0x0D };
            variants["v_const_text_tables"] = new HashSet<uint>(variants["v_const_text"]) { 0x43, 0x44, 0x45 };
            variants["v_small"] = new HashSet<uint>(variants["v_const_text_tables"]);
            foreach (Span s in spans)
            {
                if (s.ValueLength <= 8)
                {
                    variants["v_small"].Add(s.Tag);
                }
            }

            variants["v_noblobs"] = new HashSet<uint>(spans.Where(s => s.ValueLength <= 256).Select(s => s.Tag));

            foreach (KeyValuePair<string, HashSet<uint>> v in variants)
            {
                byte[] prologue = FilterPrologue(plain, spans, v.Value);
                byte[] outPlain = new byte[prologue.Length + components.Length];
                Buffer.BlockCopy(prologue, 0, outPlain, 0, prologue.Length);
                Buffer.BlockCopy(components, 0, outPlain, prologue.Length, components.Length);
                byte[] fml = FmlDecryptor.Encode(outPlain, LayoutWriter.DefaultPassword, LayoutWriter.DefaultChunkSize, phase1: true);
                File.WriteAllBytes(Path.Combine(outDir, v.Key + ".fml"), fml);
            }

            // The actual reconstruction prologue (machine.json + map + constants), with the
            // original component stream kept, to compare like-for-like.
            if (gamPath is not null && File.Exists(gamPath))
            {
                if (GameConfigDecoder.TryDecode(fmlPath, gamPath, plain, null, out GameConfigDecodeResult cfg, out _))
                {
                    string system = cfg.MachineJson is null ? null : SystemOf(cfg.MachineJson);
                    if (system is not null && GameConfigMapRegistry.TryGet(system, out GameConfigMap map, out _))
                    {
                        PrologueSynthesizer.Result synth = PrologueSynthesizer.Synthesize(cfg.MachineJson, map);
                        byte[] outPlain = new byte[synth.Prologue.Length + components.Length];
                        Buffer.BlockCopy(synth.Prologue, 0, outPlain, 0, synth.Prologue.Length);
                        Buffer.BlockCopy(components, 0, outPlain, synth.Prologue.Length, components.Length);
                        byte[] fml = FmlDecryptor.Encode(outPlain, LayoutWriter.DefaultPassword, LayoutWriter.DefaultChunkSize, phase1: true);
                        File.WriteAllBytes(Path.Combine(outDir, "v_synth.fml"), fml);
                        File.WriteAllLines(Path.Combine(outDir, "v_synth.unsupported.txt"), synth.Unsupported);
                    }
                }
            }

            if (gamPath is not null && File.Exists(gamPath))
            {
                File.Copy(gamPath, Path.Combine(outDir, Path.GetFileNameWithoutExtension(fmlPath) + ".gam"), overwrite: true);
                foreach (string variant in Directory.EnumerateFiles(outDir, "v_*.fml"))
                {
                    File.Copy(gamPath, Path.ChangeExtension(variant, ".gam"), overwrite: true);
                }
            }

            Console.Out.WriteLine("prologue tags: " + string.Join(" ", spans.Select(s => $"0x{s.Tag:X2}({s.ValueLength})")));
            Console.Out.WriteLine("prologue end: 0x" + prologueEnd.ToString("X"));
            foreach (string f in Directory.EnumerateFiles(outDir, "v_*.fml").OrderBy(x => x))
            {
                Console.Out.WriteLine("wrote " + Path.GetFileName(f));
            }

            return 0;
        }

        private static string SystemOf(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("System", out System.Text.Json.JsonElement s)
                ? s.GetString()
                : null;
        }

        private static (List<Span> Spans, int End) WalkPrologue(byte[] plain)
        {
            var spans = new List<Span>();
            int pos = 0;
            while (pos + 8 <= plain.Length)
            {
                int start = pos;
                uint tag = BitConverter.ToUInt32(plain, pos);
                uint len = BitConverter.ToUInt32(plain, pos + 4);
                pos += 8;
                if (tag == 0xFFFFFFFF)
                {
                    return (spans, pos);
                }

                if (len > plain.Length - pos)
                {
                    throw new InvalidDataException("TLV overrun");
                }

                pos += (int)len;
                if (LengthPrefixedStringTableScanner.IsFileLevelTag(tag))
                {
                    pos += LengthPrefixedStringTableScanner.MeasureSpan(plain, pos, hostTagKeyByte: (byte)tag);
                }

                spans.Add(new Span(tag, start, pos, (int)len));
            }

            return (spans, pos);
        }

        private static byte[] FilterPrologue(byte[] plain, List<Span> spans, HashSet<uint> tags)
        {
            using var ms = new MemoryStream();
            foreach (Span s in spans)
            {
                if (tags.Contains(s.Tag))
                {
                    ms.Write(plain, s.Start, s.End - s.Start);
                }
            }

            Span<byte> term = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(term, 0xFFFFFFFF);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(term.Slice(4), 0);
            ms.Write(term);
            return ms.ToArray();
        }
    }
}
