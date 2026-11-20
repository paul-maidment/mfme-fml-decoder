using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Model.Component;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>
    /// Encodes one model value back to its on-disk tag value bytes. Inverse of the decoder's
    /// <c>ParseValue</c> for the roles the encoder supports.
    /// </summary>
    internal static class TagValueWriter
    {
        public static bool TryEncode(
            BaseComponent component,
            TagInfo info,
            ComponentSpecialValues specials,
            out byte[] bytes,
            bool legacyAnsiText = false)
        {
            bytes = null;
            if (component is null || info is null)
            {
                return false;
            }

            string name = info.AttributeName;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            // Values the decoder folded into properties/other dictionaries (ReelMode, coin-note ids,
            // Border raw blobs, SevenSeg style ids, programmable tables) take priority.
            if (DerivedTagValues.TryGet(component, info, specials, out bytes))
            {
                return true;
            }

            switch (info.Role)
            {
                case ValueRole.UINT32:
                    if (component.UInt32s.TryGetValue(name, out uint u32))
                    {
                        bytes = WriteU32(u32);
                        return true;
                    }
                    return false;

                case ValueRole.INT32:
                    if (component.Int32s.TryGetValue(name, out int i32))
                    {
                        bytes = WriteI32(i32);
                        return true;
                    }
                    return false;

                case ValueRole.UINT16:
                    if (component.UInt16s.TryGetValue(name, out ushort u16))
                    {
                        bytes = WriteU16(u16);
                        return true;
                    }
                    return false;

                case ValueRole.BYTE:
                    if (component.Bytes.TryGetValue(name, out byte b))
                    {
                        bytes = new[] { b };
                        return true;
                    }
                    return false;

                case ValueRole.BOOLEAN:
                    if (component.Booleans.TryGetValue(name, out bool flag))
                    {
                        bytes = WriteBoolean(flag, info.Length);
                        return true;
                    }
                    return false;

                case ValueRole.FLOAT:
                    if (component.Floats.TryGetValue(name, out float f))
                    {
                        bytes = WriteF32(f);
                        return true;
                    }
                    return false;

                case ValueRole.ARGB_COLOR:
                    if (component.Colours.TryGetValue(name, out string colour))
                    {
                        bytes = WriteColour(colour);
                        return true;
                    }
                    return false;

                case ValueRole.TEXT:
                    if (component.Strings.TryGetValue(name, out string text))
                    {
                        bytes = WriteText(text, legacyAnsiText);
                        return true;
                    }
                    return false;

                case ValueRole.FONT:
                    if (component.Fonts.TryGetValue(name, out FontTagEntry font))
                    {
                        bytes = WriteFont(font);
                        return true;
                    }
                    return false;

                case ValueRole.BITMAP:
                    string purpose = TagPurpose.Slugify(name);
                    if (purpose is not null && component.Images.TryGetValue(purpose, out BitmapEntry image))
                    {
                        bytes = image.Bytes;
                        if (image.Synthesized && bytes is { Length: > 1 }
                            && !(bytes[0] == 0x42 && bytes[1] == 0x4D)
                            && image.Width > 0 && image.Height > 0
                            && (long)bytes.Length == (long)image.Width * image.Height * 4)
                        {
                            bytes = WrapRawPixelsAsBmp32(image.Width, image.Height, bytes);
                        }
                        return bytes is { Length: > 0 };
                    }
                    return false;

                case ValueRole.SUBLAMP_COUNT:
                    if (specials?.SublampTable is not null)
                    {
                        bytes = WriteU32((uint)specials.SublampTable.Count);
                        return true;
                    }
                    return false;

                case ValueRole.LAMP_SUBLAMP_TABLE:
                    if (specials?.SublampTable is { Count: > 0 } table)
                    {
                        bytes = WriteLampSublampTable(table);
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        private static byte[] WriteU32(uint value)
        {
            byte[] b = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(b, value);
            return b;
        }

        private static byte[] WriteI32(int value)
        {
            byte[] b = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(b, value);
            return b;
        }

        private static byte[] WriteU16(ushort value)
        {
            byte[] b = new byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(b, value);
            return b;
        }

        private static byte[] WriteF32(float value)
        {
            byte[] b = new byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(b, value);
            return b;
        }

        private static byte[] WriteBoolean(bool value, int length)
        {
            int size = length is 1 or 2 or 4 ? length : 1;
            byte[] b = new byte[size];
            if (value)
            {
                b[0] = 0x01;
            }
            return b;
        }

        // Wire bytes are [B, G, R, A]; the model stores "#RRGGBBAA".
        private static byte[] WriteColour(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || hex.Length != 9)
            {
                return new byte[4];
            }

            byte r = ParseHexByte(hex, 1);
            byte g = ParseHexByte(hex, 3);
            byte b = ParseHexByte(hex, 5);
            byte a = ParseHexByte(hex, 7);
            return new[] { b, g, r, a };
        }
        // Build a minimal 32bpp bottom-up BMP (BITMAPINFOHEADER) from raw BGRA pixels.
        private static byte[] WrapRawPixelsAsBmp32(int width, int height, byte[] pixels)
        {
            const int offBits = 54;
            int size = offBits + pixels.Length;
            byte[] bmp = new byte[size];
            bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2, 4), size);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10, 4), offBits);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14, 4), 40);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18, 4), width);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22, 4), height);
            BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(26, 2), 1);
            BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(28, 2), 32);
            BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(34, 4), pixels.Length);
            Array.Copy(pixels, 0, bmp, offBits, pixels.Length);
            return bmp;
        }

        // [charCount:u32][UTF-16LE chars]  (or, for legacy Windows-1252 profiles,
        // [byteCount:u32][Windows-1252 bytes]).
        private static byte[] WriteText(string value, bool legacyAnsiText)
        {
            value ??= string.Empty;
            if (legacyAnsiText)
            {
                byte[] ansi = EncodeWindows1252(value);
                byte[] ansiResult = new byte[4 + ansi.Length];
                BinaryPrimitives.WriteUInt32LittleEndian(ansiResult, (uint)ansi.Length);
                Array.Copy(ansi, 0, ansiResult, 4, ansi.Length);
                return ansiResult;
            }

            byte[] utf16 = Encoding.Unicode.GetBytes(value);
            byte[] result = new byte[4 + utf16.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)value.Length);
            Array.Copy(utf16, 0, result, 4, utf16.Length);
            return result;
        }

        // Inverse of the decoder's Windows-1252 table (no code-page provider needed).
        private static readonly char[] Windows1252High =
        {
            '\u20AC','\u0081','\u201A','\u0192','\u201E','\u2026','\u2020','\u2021',
            '\u02C6','\u2030','\u0160','\u2039','\u0152','\u008D','\u017D','\u008F',
            '\u0090','\u2018','\u2019','\u201C','\u201D','\u2022','\u2013','\u2014',
            '\u02DC','\u2122','\u0161','\u203A','\u0153','\u009D','\u017E','\u0178',
        };

        private static byte[] EncodeWindows1252(string value)
        {
            byte[] bytes = new byte[value.Length];
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c <= 0x7F || (c >= 0xA0 && c <= 0xFF))
                {
                    bytes[i] = (byte)c;
                    continue;
                }

                int hit = Array.IndexOf(Windows1252High, c);
                bytes[i] = hit >= 0 ? (byte)(0x80 + hit) : (byte)'?';
            }

            return bytes;
        }

        // [nameLen:u32][name][fontSize:u32][scriptStyle:u32][padding:1][fontStyle:1]
        private static byte[] WriteFont(FontTagEntry font)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(font.FontName ?? string.Empty);
            using var ms = new MemoryStream();
            byte[] nameLen = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(nameLen, (uint)nameBytes.Length);
            ms.Write(nameLen);
            ms.Write(nameBytes);

            byte[] tail = new byte[4 + 4 + 1 + 1];
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(0, 4), font.FontSize);
            BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(4, 4), BuildScriptStyleDword(font));
            tail[8] = 0x00; // padding
            tail[9] = font.FontStyle;
            ms.Write(tail);

            return ms.ToArray();
        }

        private static uint BuildScriptStyleDword(FontTagEntry font)
        {
            // TextColour is "#RRGGBBFF"; the dword stores COLORREF BGR in its high 24 bits
            // and the script index in the low byte.
            string hex = font.TextColour;
            byte r = 0, g = 0, b = 0;
            if (!string.IsNullOrEmpty(hex) && hex[0] == '#' && hex.Length >= 7)
            {
                r = ParseHexByte(hex, 1);
                g = ParseHexByte(hex, 3);
                b = ParseHexByte(hex, 5);
            }

            uint bgr24 = (uint)(b << 16 | g << 8 | r);
            return bgr24 << 8 | font.ScriptStyleRaw;
        }

        private static byte[] WriteLampSublampTable(IReadOnlyList<LampSublampTableEntry> table)
        {
            byte[] b = new byte[table.Count * 4];
            for (int i = 0; i < table.Count; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    b.AsSpan(i * 4, 4),
                    table[i].SublampNumber);
            }
            return b;
        }

        private static byte ParseHexByte(string hex, int offset)
        {
            return byte.Parse(hex.Substring(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }
}

