using System;
using System.Buffers.Binary;
using System.IO;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>Little-endian primitive writers shared by the model-to-stream encoder.</summary>
    internal static class WireWriter
    {
        public static void U8(Stream stream, byte value) => stream.WriteByte(value);

        public static void U16(Stream stream, ushort value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        public static void U32(Stream stream, uint value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        public static void I32(Stream stream, int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            stream.Write(buffer);
        }

        public static void F32(Stream stream, float value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
            stream.Write(buffer);
        }

        public static void Bytes(Stream stream, byte[] value)
        {
            if (value is { Length: > 0 })
            {
                stream.Write(value, 0, value.Length);
            }
        }
    }
}
