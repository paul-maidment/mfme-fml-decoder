using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MfmeFmlDecoder.Decryption;
using MfmeFmlDecoder.GameConfig;
using Xunit;

namespace MfmeFmlDecoder.UnitTests
{
    public class FmlRepackRoundtripTests
    {
        private static readonly string Mpu5 = Path.Combine(
            AppContext.BaseDirectory, "gameconfig_smoke", "MPU5", "layout.fml");

        [Fact]
        public void Repack_WithoutEdits_PlaintextMatches_StrictPassword()
        {
            byte[] fml = File.ReadAllBytes(Mpu5);
            byte[] plain = FmlDecryptor.Decrypt(fml);
            byte[] repacked = FmlDecryptor.RepackAsSingleChunk(fml, plain);

            string pw = InvokeDerive(repacked);
            byte[] again = InvokeDecryptAll(repacked, pw);
            Assert.Equal(plain, again);

            // Also: public Decrypt (with fallback) 
            Assert.Equal(plain, FmlDecryptor.Decrypt(repacked));
        }

        [Fact]
        public void Ecl_EncryptDecrypt_Roundtrips()
        {
            var t = typeof(FmlDecryptor);
            var eclEnc = t.GetMethod("EclEncrypt", BindingFlags.NonPublic | BindingFlags.Static);
            var eclDec = t.GetMethod("EclDecrypt", BindingFlags.NonPublic | BindingFlags.Static);
            var encEcb = t.GetMethod("EncryptEcb", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(eclEnc);

            byte[] key = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
            byte[] ivSrc = Enumerable.Repeat((byte)0xFF, 16).ToArray();
            byte[] iv = (byte[])encEcb.Invoke(null, new object[] { key, ivSrc });
            byte[] plain = Enumerable.Range(0, 100).Select(i => (byte)(i * 7)).ToArray();

            byte[] cipher = (byte[])eclEnc.Invoke(null, new object[] { plain, key, iv });
            Assert.Equal(plain.Length, cipher.Length);

            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Mode = System.Security.Cryptography.CipherMode.ECB;
            aes.Padding = System.Security.Cryptography.PaddingMode.None;
            aes.Key = key;
            using var dec = aes.CreateDecryptor();
            using var enc = aes.CreateEncryptor();
            byte[] outBuf = new byte[cipher.Length];
            eclDec.Invoke(null, new object[] { cipher, 0, cipher.Length, dec, enc, iv, outBuf });
            Assert.Equal(plain, outBuf);
        }

        [Fact]
        public void RemoveTag_0x9B_ThenRepack_TagGone_Strict()
        {
            byte[] fml = File.ReadAllBytes(Mpu5);
            byte[] plain = FmlDecryptor.Decrypt(fml);
            Assert.True(FileLevelTagBag.Parse(plain).ContainsKey(0x9B));

            byte[] edited = FileLevelTagBag.RemoveTag(plain, 0x9B, out int n);
            Assert.Equal(1, n);
            Assert.False(FileLevelTagBag.Parse(edited).ContainsKey(0x9B));

            byte[] repacked = FmlDecryptor.RepackAsSingleChunk(fml, edited);
            byte[] again = InvokeDecryptAll(repacked, InvokeDerive(repacked));
            Assert.False(FileLevelTagBag.Parse(again).ContainsKey(0x9B));

            uint crcStored = BitConverter.ToUInt32(repacked, 136);
            Assert.Equal(FmlDecryptor.ComputeChunkCrc32(edited), crcStored);
            Assert.Equal(1u, BitConverter.ToUInt32(repacked, 12));
        }

        [Fact]
        public void Mpu5_Original_ChunkCrcMatchesPlaintext()
        {
            byte[] fml = File.ReadAllBytes(Mpu5);
            byte[] plain = FmlDecryptor.Decrypt(fml);
            Assert.Equal(BitConverter.ToUInt32(fml, 136), FmlDecryptor.ComputeChunkCrc32(plain));
        }

        private static string InvokeDerive(byte[] fml)
        {
            var m = typeof(FmlDecryptor).GetMethod("DerivePassword", BindingFlags.NonPublic | BindingFlags.Static);
            return (string)m.Invoke(null, new object[] { fml });
        }

        private static byte[] InvokeDecryptAll(byte[] fml, string password)
        {
            var m = typeof(FmlDecryptor).GetMethod("DecryptAllChunks", BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                return (byte[])m.Invoke(null, new object[] { fml, password, null });
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException ?? ex;
            }
        }
    }
}
