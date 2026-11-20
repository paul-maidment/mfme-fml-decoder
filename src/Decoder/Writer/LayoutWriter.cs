using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.src.Model;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>
    /// Orchestrates model-to-FML writing: reuse the source file-level TLV prologue verbatim
    /// (updating its MFME version tag to 20.1), rebuild the component stream from the model,
    /// then wrap the plaintext in a fresh AACS container via <see cref="FmlDecryptor.Encode"/>.
    /// </summary>
    internal static class LayoutWriter
    {
        internal const string DefaultPassword = "b67de657";
        internal const int DefaultChunkSize = 524288;

        private const uint VersionTag = 0x2F;
        private static readonly byte[] V20_1VersionValue = { (byte)'2', (byte)'0', (byte)'.', (byte)'1' };
        private static readonly Dictionary<uint, int> GroupCounts = new()
        {
            [0x43] = 8,
            [0x44] = 8,
            [0x45] = 4,
        };

        /// <summary>
        /// Returns the prologue TLV with its MFME version tag (0x2F) set to "20.1". This is the
        /// tag that tells MFME/our reader which component profiles to use; without it an old
        /// source's prologue would still claim v5 while the components are in 20.1 form.
        /// </summary>
        internal static byte[] WithV20_1VersionTag(byte[] prologue)
        {
            if (prologue is null || prologue.Length < 8) return prologue;

            byte[] bytes = (byte[])prologue.Clone();
            int term = prologue.Length - 8; // 0xFFFFFFFF terminator is the last 8 bytes of the prologue

            int pos = 0;
            while (pos + 8 <= term)
            {
                uint tag = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos, 4));
                if (tag == 0xFFFFFFFF) break;

                uint len0 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 4, 4));
                int p = pos + 8 + (int)len0;
                int groups = GroupCounts.TryGetValue(tag, out int gc) ? gc : 1;
                for (int g = 1; g < groups && p + 4 <= bytes.Length; g++)
                {
                    uint len = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(p, 4));
                    p += 4 + (int)len;
                }

                if (tag == VersionTag)
                {
                    Array.Copy(V20_1VersionValue, 0, bytes, pos + 8, V20_1VersionValue.Length);
                    return bytes;
                }

                if (p <= pos) break;
                pos = p;
            }

            // No version tag: insert 0x2F <len=4> "20.1" immediately before the terminator.
            byte[] ins = new byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(ins.AsSpan(0, 4), VersionTag);
            BinaryPrimitives.WriteUInt32LittleEndian(ins.AsSpan(4, 4), (uint)V20_1VersionValue.Length);
            Array.Copy(V20_1VersionValue, 0, ins, 8, V20_1VersionValue.Length);

            byte[] result = new byte[prologue.Length + ins.Length];
            Array.Copy(bytes, 0, result, 0, term);
            Array.Copy(ins, 0, result, term, ins.Length);
            Array.Copy(bytes, term, result, term + ins.Length, prologue.Length - term);
            return result;
        }

        public static byte[] WriteFml(
            byte[] prologue,
            Layout layout,
            MfmeVersion version,
            bool valueBasedSparse = false,
            string password = DefaultPassword,
            int chunkSize = DefaultChunkSize,
            bool phase1 = true,
            bool emitAllModelled = false)
        {
            if (prologue is null) throw new ArgumentNullException(nameof(prologue));
            if (layout is null) throw new ArgumentNullException(nameof(layout));

            byte[] componentStream = ComponentStreamWriter.Write(layout.Components, version, valueBasedSparse, emitAllModelled);

            byte[] plaintext = new byte[prologue.Length + componentStream.Length];
            Buffer.BlockCopy(prologue, 0, plaintext, 0, prologue.Length);
            Buffer.BlockCopy(componentStream, 0, plaintext, prologue.Length, componentStream.Length);

            return FmlDecryptor.Encode(plaintext, password, chunkSize, phase1);
        }

        public static byte[] WriteFml(
            byte[] originalPlaintext,
            int prologueLength,
            Layout layout,
            MfmeVersion version,
            bool valueBasedSparse = false,
            string password = DefaultPassword,
            int chunkSize = DefaultChunkSize,
            bool phase1 = true)
        {
            if (originalPlaintext is null) throw new ArgumentNullException(nameof(originalPlaintext));
            if (prologueLength < 0 || prologueLength > originalPlaintext.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(prologueLength));
            }

            byte[] prologue = new byte[prologueLength];
            Buffer.BlockCopy(originalPlaintext, 0, prologue, 0, prologueLength);
            prologue = WithV20_1VersionTag(prologue);
            return WriteFml(prologue, layout, version, valueBasedSparse, password, chunkSize, phase1);
        }
    }
}
