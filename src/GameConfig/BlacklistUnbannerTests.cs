using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MfmeFmlDecoder.Decryption;
using Xunit;

namespace MfmeFmlDecoder.GameConfig
{
    public class BlacklistUnbannerTests
    {
        private static readonly byte[] CleanField2 = { 0xE6, 0xA0, 0xD5, 0xE5, 0xB1, 0xDF };
        private static readonly byte[] Timestamp = { 1, 2, 3, 4, 5, 6, 7, 8 };

        [Fact]
        public void Unban_ClearsExactNeedleInField6()
        {
            byte[] plain = PlaintextWith(
                Record(new byte[] { 0x11 }, CleanField2, new byte[] { 0x22 }, new byte[] { 0x33 },
                    new byte[] { 0x14, 0x00, 0x00, 0x00 }, BlacklistUnbanner.ExactNeedle));

            UnbanResult result = BlacklistUnbanner.Unban(plain);

            Assert.True(result.Changed);
            UnbanEdit edit = Assert.Single(result.Edits);
            Assert.Equal("field 6", edit.Field);
            Assert.Equal("exact needle", edit.Kind);
            Assert.False(BlacklistUnbanner.Unban(result.Plaintext).Changed);
        }

        [Fact]
        public void Unban_ClearsSubstringNeedleInField2()
        {
            byte[] f2 = Prefix(6).Concat(BlacklistUnbanner.SubstringNeedles[1]).ToArray();
            byte[] plain = PlaintextWith(
                Record(new byte[] { 0x11 }, f2, new byte[] { 0x22 }, new byte[] { 0x33 },
                    new byte[] { 0x14, 0x00, 0x00, 0x00 }, Array.Empty<byte>()));

            UnbanResult result = BlacklistUnbanner.Unban(plain);

            Assert.True(result.Changed);
            UnbanEdit edit = Assert.Single(result.Edits);
            Assert.Equal("field 2", edit.Field);
            Assert.StartsWith("substring needle", edit.Kind);
            Assert.False(BlacklistUnbanner.Unban(result.Plaintext).Changed);
        }

        [Fact]
        public void Unban_OnlyEverChangesOneBytePerNeedleHit()
        {
            byte[] f2 = Prefix(3).Concat(BlacklistUnbanner.SubstringNeedles[0]).ToArray();
            byte[] plain = PlaintextWith(
                Record(new byte[] { 0x11 }, f2, new byte[] { 0x22 }, new byte[] { 0x33 },
                    new byte[] { 0x14 }, Array.Empty<byte>()));

            UnbanResult result = BlacklistUnbanner.Unban(plain);

            UnbanEdit edit = Assert.Single(result.Edits);
            Assert.Single(edit.Replacement);
            Assert.Equal(1, DifferenceCount(plain, result.Plaintext));
            Assert.Equal(plain[edit.Offset], edit.Original[0]);
        }

        [Fact]
        public void Unban_LeavesCleanPayloadUntouched()
        {
            byte[] plain = PlaintextWith(
                Record(new byte[] { 0x11 }, CleanField2, new byte[] { 0x22 }, new byte[] { 0x33 },
                    new byte[] { 0x14 }, new byte[] { 0x44, 0x55 }));

            UnbanResult result = BlacklistUnbanner.Unban(plain);

            Assert.False(result.Changed);
            Assert.Empty(result.Edits);
            Assert.Same(plain, result.Plaintext);
            Assert.Equal(1, result.PayloadsScanned);
            Assert.Equal(1, result.RecordsScanned);
        }

        [Fact]
        public void Unban_ScansEveryRecordAndReportsTheOffendingOne()
        {
            byte[] blacklisted = Record(new byte[] { 0x11 }, CleanField2, new byte[] { 0x22 },
                new byte[] { 0x33 }, new byte[] { 0x14 }, BlacklistUnbanner.ExactNeedle);
            byte[] clean = Record(new byte[] { 0x11 }, CleanField2, new byte[] { 0x22 },
                new byte[] { 0x33 }, new byte[] { 0x14 }, new byte[] { 0x44 });

            byte[] plain = PlaintextWith(clean, clean, blacklisted, clean);

            UnbanResult result = BlacklistUnbanner.Unban(plain);

            Assert.Equal(4, result.RecordsScanned);
            Assert.Equal(2, Assert.Single(result.Edits).RecordIndex);
        }

        [Fact]
        public void Unban_IgnoresNeedlesOutsideTheLayoutPayload()
        {
            // Same needle, but stored under a different file-level tag.
            byte[] payload = Record(new byte[] { 0x11 }, CleanField2, new byte[] { 0x22 },
                new byte[] { 0x33 }, new byte[] { 0x14 }, BlacklistUnbanner.ExactNeedle);

            using var ms = new MemoryStream();
            WriteTag(ms, 0x2F, Encoding.ASCII.GetBytes("20.1"));
            WriteTag(ms, 0x64, payload);
            ms.Write(BitConverter.GetBytes(FileLevelTagBag.TerminationTag));

            UnbanResult result = BlacklistUnbanner.Unban(ms.ToArray());

            Assert.False(result.Changed);
            Assert.Equal(0, result.PayloadsScanned);
        }

        [Fact]
        public void Unban_WithoutLayoutTagWarnsInsteadOfThrowing()
        {
            using var ms = new MemoryStream();
            WriteTag(ms, 0x2F, Encoding.ASCII.GetBytes("20.1"));
            ms.Write(BitConverter.GetBytes(FileLevelTagBag.TerminationTag));

            UnbanResult result = BlacklistUnbanner.Unban(ms.ToArray());

            Assert.False(result.Changed);
            Assert.NotEmpty(result.Warnings);
        }

        [Fact]
        public void Unban_TruncatedRecordWarnsAndKeepsEarlierEdits()
        {
            byte[] good = Record(new byte[] { 0x11 }, CleanField2, new byte[] { 0x22 },
                new byte[] { 0x33 }, new byte[] { 0x14 }, BlacklistUnbanner.ExactNeedle);

            // A second record whose field 3 length overruns the payload.
            byte[] truncated =
            {
                0x01, 0x00, 0x00, 0x00, 0x41,
                0x01, 0x00, 0x00, 0x00, 0x42,
                0xFF, 0x00, 0x00, 0x00,
            };

            UnbanResult result = BlacklistUnbanner.Unban(
                PlaintextWith(good.Concat(truncated).ToArray()));

            Assert.True(result.Changed);
            Assert.NotEmpty(result.Warnings);
            Assert.False(BlacklistUnbanner.Unban(result.Plaintext).Changed);
        }

        [Fact]
        public void Unban_IgnoresNeedlesInFieldsTheLoaderDoesNotCompare()
        {
            // Field 1, 3, 4 and 5 are never searched by the loader.
            byte[] plain = PlaintextWith(
                Record(BlacklistUnbanner.ExactNeedle, CleanField2, BlacklistUnbanner.ExactNeedle,
                    BlacklistUnbanner.SubstringNeedles[2], BlacklistUnbanner.ExactNeedle,
                    new byte[] { 0x44 }));

            UnbanResult result = BlacklistUnbanner.Unban(plain);

            Assert.False(result.Changed);
        }

        [Fact]
        public void Unban_OnlySeesBytesBeforeTheFirstNull()
        {
            // Field 2 stores the needle after a NUL, so the loader's C string never
            // reaches it and there is nothing to clear.
            byte[] f2 = Prefix(2).Concat(new byte[] { 0x00 })
                .Concat(BlacklistUnbanner.SubstringNeedles[3]).ToArray();

            byte[] plain = PlaintextWith(
                Record(new byte[] { 0x11 }, f2, new byte[] { 0x22 }, new byte[] { 0x33 },
                    new byte[] { 0x14 }, Array.Empty<byte>()));

            Assert.False(BlacklistUnbanner.Unban(plain).Changed);
        }

        [Fact]
        public void Unban_SurvivesEncryptDecryptRepack()
        {
            byte[] payload = Record(new byte[] { 0x11 },
                Prefix(4).Concat(BlacklistUnbanner.SubstringNeedles[4]).ToArray(),
                new byte[] { 0x22 }, new byte[] { 0x33 }, new byte[] { 0x14 },
                BlacklistUnbanner.ExactNeedle);
            byte[] plain = PlaintextWith(payload);

            byte[] fml = FmlDecryptor.Encode(plain, "b67de657", phase1: true);
            byte[] decoded = FmlDecryptor.Decrypt(fml);

            UnbanResult result = BlacklistUnbanner.Unban(decoded);
            Assert.Equal(2, result.Edits.Count);

            byte[] repacked = FmlDecryptor.RepackAsSingleChunk(fml, result.Plaintext);
            byte[] after = FmlDecryptor.Decrypt(repacked);

            Assert.False(BlacklistUnbanner.Unban(after).Changed);
            Assert.Equal(2, DifferenceCount(decoded, after));
        }

        [Fact]
        public void Unban_PicksMutationThatHidesEveryNeedle()
        {
            // Two needles overlapping the same field: both must end up hidden.
            byte[] f2 = Prefix(2)
                .Concat(BlacklistUnbanner.SubstringNeedles[1])
                .Concat(BlacklistUnbanner.SubstringNeedles[2])
                .ToArray();

            byte[] plain = PlaintextWith(
                Record(new byte[] { 0x11 }, f2, new byte[] { 0x22 }, new byte[] { 0x33 },
                    new byte[] { 0x14 }, Array.Empty<byte>()));

            UnbanResult result = BlacklistUnbanner.Unban(plain);

            Assert.Equal(2, result.Edits.Count);
            Assert.False(BlacklistUnbanner.Unban(result.Plaintext).Changed);
        }

        private static byte[] Prefix(int length)
        {
            var result = new byte[length];
            for (int i = 0; i < length; i++)
                result[i] = (byte)(0xE0 + i);
            return result;
        }

        private static byte[] Record(
            byte[] field1,
            byte[] field2,
            byte[] field3,
            byte[] field4,
            byte[] field5,
            byte[] field6)
        {
            using var ms = new MemoryStream();
            Blob(ms, field1);
            Blob(ms, field2);
            Blob(ms, field3);
            Blob(ms, field4);
            ms.Write(Timestamp);
            Blob(ms, field5);

            // The loader only reads field 6 while payload bytes remain.
            if (field6.Length > 0)
                Blob(ms, field6);

            return ms.ToArray();
        }

        private static void Blob(Stream stream, byte[] value)
        {
            stream.Write(BitConverter.GetBytes((uint)value.Length));
            stream.Write(value);
        }

        private static byte[] PlaintextWith(params byte[][] records)
        {
            using var ms = new MemoryStream();
            WriteTag(ms, 0x2F, Encoding.ASCII.GetBytes("20.1"));
            WriteTag(ms, BlacklistUnbanner.LayoutPayloadTag, records.SelectMany(r => r).ToArray());
            ms.Write(BitConverter.GetBytes(FileLevelTagBag.TerminationTag));
            return ms.ToArray();
        }

        private static void WriteTag(Stream stream, uint tag, byte[] value)
        {
            stream.Write(BitConverter.GetBytes(tag));
            stream.Write(BitConverter.GetBytes((uint)value.Length));
            stream.Write(value);
        }

        private static int DifferenceCount(byte[] a, byte[] b)
        {
            Assert.Equal(a.Length, b.Length);
            int count = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    count++;
            }

            return count;
        }
    }
}
