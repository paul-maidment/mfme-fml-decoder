using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.src.Model.Component;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>
    /// Serializes the decoded component list back to the tagged component stream that follows
    /// the file-level TLV prologue. Each record is <c>u32 kind + u32 (payloadLength + 8) + payload</c>.
    /// </summary>
    internal static class ComponentStreamWriter
    {
        public static byte[] Write(IReadOnlyList<BaseComponent> components, MfmeVersion version, bool valueBasedSparse = false)
        {
            using var stream = new MemoryStream();
            if (components is null)
            {
                return stream.ToArray();
            }

            foreach (BaseComponent component in components)
            {
                byte[] payload = ComponentPayloadWriter.WritePayload(component, version, valueBasedSparse);
                Span<byte> header = stackalloc byte[8];
                BinaryPrimitives.WriteUInt32LittleEndian(header, ComponentKind.ResolveId(component));
                BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4), (uint)(payload.Length + 8));
                stream.Write(header);
                stream.Write(payload, 0, payload.Length);
            }

            return stream.ToArray();
        }
    }
}

