using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Decoder.Component.Helper;
using MfmeFmlDecoder.src.Model.Component;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>
    /// Reconstructs tag values that the decoder derives into properties/other dictionaries and
    /// therefore no longer stores under the tag's attribute name (e.g. ReelMode from
    /// DblSymbols/IGTReel, coin-note ids from the resolved names, Border bounding-box raw blobs).
    /// Consulted before the generic <see cref="TagValueWriter"/>.
    /// </summary>
    internal static class DerivedTagValues
    {
        private static readonly Dictionary<byte, string> SevenSegStyleOptions = new()
        {
            [0x00] = "Style 1",
            [0x01] = "Style 2",
            [0x02] = "Style 3",
        };

        private static readonly Dictionary<string, byte> SevenSegStyleIdsByDisplayName = Invert(SevenSegStyleOptions);

        public static bool TryGet(BaseComponent component, TagInfo info, ComponentSpecialValues specials, out byte[] bytes)
        {
            bytes = null;
            string name = info?.AttributeName;
            if (component is null || string.IsNullOrEmpty(name))
            {
                return false;
            }

            switch (component)
            {
                case Reel reel:
                    return TryReel(reel, name, info.Role, out bytes);

                case Lamp lamp:
                    return TryCoinNote(lamp.Strings, name, info.Role, out bytes);

                case Button button:
                    return TryCoinNote(button.Strings, name, info.Role, out bytes);

                case Label label:
                    // The decoder drops an undefined Lamp (-2) from the model entirely. Re-emit
                    // -2 when no Lamp value survived, so MFME keeps treating it as undefined
                    // instead of defaulting to 0.
                    if (name == "Lamp" && info.Role == ValueRole.INT32
                        && !label.Int32s.ContainsKey("Lamp"))
                    {
                        bytes = new byte[4];
                        BinaryPrimitives.WriteInt32LittleEndian(bytes, -2);
                        return true;
                    }
                    return false;

                case Border border:
                    return TryBorder(border, name, info.Role, out bytes);

                case SevenSeg sevenSeg:
                    if (name == "SelectedStyleId" && info.Role == ValueRole.BYTE
                        && TryReverseStyle(sevenSeg.Strings, out byte styleId))
                    {
                        bytes = new[] { styleId };
                        return true;
                    }
                    return false;

                case SevenSegBlock sevenSegBlock:
                    return TrySevenSegBlock(sevenSegBlock, name, info, out bytes);
            }

            return false;
        }

        private static bool TryReel(Reel reel, string name, ValueRole role, out byte[] bytes)
        {
            bytes = null;
            if (name == "ReelMode" && role == ValueRole.UINT32)
            {
                bool dbl = reel.Booleans.TryGetValue("DblSymbols", out bool d) && d;
                bool igt = reel.Booleans.TryGetValue("IGTReel", out bool i) && i;
                uint mode = dbl ? 2u : (igt ? 1u : 0u);
                bytes = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, mode);
                return true;
            }

            if (name == "WidthDiff" && role == ValueRole.INT32
                && reel.Int32s.TryGetValue("WidthDiff", out int widthDiff))
            {
                // Parser doubled the wire value for presentation; halve it back.
                bytes = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(bytes, widthDiff / 2);
                return true;
            }

            return false;
        }

        private static bool TryCoinNote(
            IReadOnlyDictionary<string, string> strings,
            string name,
            ValueRole role,
            out byte[] bytes)
        {
            bytes = null;
            if (name != "SelectedCoinNoteId" && name != "SelectedEffectId")
            {
                return false;
            }

            if (!strings.TryGetValue("SelectedCoinNote", out string coinNote)
                || !strings.TryGetValue("SelectedEffect", out string effect))
            {
                return false;
            }

            // Only when BOTH coin-note and effect are empty is the id "none" (default, tag
            // absent). If just one is empty the other is still a real default option and must
            // be derived (e.g. effect "Electronic" with no coin-note).
            if (string.IsNullOrEmpty(coinNote) && string.IsNullOrEmpty(effect))
            {
                return false;
            }

            foreach (CoinNoteOption option in CoinNoteHierarchy.Options)
            {
                if (!string.Equals(option.Name, coinNote, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (EffectOption effectOption in option.Effects)
                {
                    if (!string.Equals(effectOption.Name, effect, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (name == "SelectedCoinNoteId")
                    {
                        bytes = new byte[4];
                        BinaryPrimitives.WriteInt32LittleEndian(bytes, option.Index);
                        return true;
                    }

                    bytes = new byte[4];
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)effectOption.Index);
                    return true;
                }
            }

            return false;
        }

        private static bool TryBorder(Border border, string name, ValueRole role, out byte[] bytes)
        {
            bytes = null;
            if (role != ValueRole.RAW)
            {
                return false;
            }

            if (name == "BoundingBox" && border.Int32s.ContainsKey("BoxX"))
            {
                bytes = new byte[16];
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), border.BoxX);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), border.BoxY);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), border.BoxWidth);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12, 4), border.BoxHeight);
                return true;
            }

            if (name == "PaneSplit" && border.Int32s.ContainsKey("LeftWidth"))
            {
                bytes = new byte[16];
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), border.LeftWidth);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), border.TopHeight);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), border.RightWidth);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), border.BottomHeight);
                return true;
            }

            return false;
        }

        private static bool TrySevenSegBlock(SevenSegBlock component, string name, TagInfo info, out byte[] bytes)
        {
            bytes = null;

            if (name == "SelectedTypeId" && info.Role == ValueRole.BYTE
                && TryReverseStyle(component.Strings, out byte typeId))
            {
                bytes = new[] { typeId };
                return true;
            }

            if (info.Role == ValueRole.DIGIT_FLAG_ARRAY
                && component.DigitFlags is not null
                && component.DigitFlags.TryGetValue(name, out bool[] flags))
            {
                bytes = EncodeBoolArray(flags, info.Length);
                return true;
            }

            if (info.Role == ValueRole.PROGRAMMABLE_DIGIT_MASK)
            {
                bool[] programmable = ResolveProgrammableMask(component);
                if (programmable is not null)
                {
                    bytes = EncodeBoolArray(programmable, info.Length);
                    return true;
                }
                return false;
            }

            if (info.Role == ValueRole.PROGRAMMABLE_LAMP_TABLE
                && component.ProgrammableLampNumbers is { Count: > 0 })
            {
                bytes = EncodeProgrammableLampTable(component, info);
                return true;
            }

            return false;
        }

        private static bool[] ResolveProgrammableMask(SevenSegBlock component)
        {
            if (component.DigitFlags is not null
                && component.DigitFlags.TryGetValue("Programmable", out bool[] programmable))
            {
                return programmable;
            }

            if (component.ProgrammableLampNumbers is not null && component.ProgrammableLampNumbers.Count > 0)
            {
                int maxDigit = 0;
                foreach (int digit in component.ProgrammableLampNumbers.Keys)
                {
                    if (digit + 1 > maxDigit)
                    {
                        maxDigit = digit + 1;
                    }
                }

                bool[] mask = new bool[maxDigit];
                foreach (int digit in component.ProgrammableLampNumbers.Keys)
                {
                    if (digit >= 0 && digit < mask.Length)
                    {
                        mask[digit] = true;
                    }
                }

                return mask;
            }

            return null;
        }

        private static byte[] EncodeBoolArray(bool[] flags, int length)
        {
            int size = length > 0 ? length : flags.Length;
            byte[] bytes = new byte[size];
            for (int i = 0; i < size && i < flags.Length; i++)
            {
                bytes[i] = flags[i] ? (byte)0x01 : (byte)0x00;
            }

            return bytes;
        }

        private static byte[] EncodeProgrammableLampTable(SevenSegBlock component, TagInfo info)
        {
            int digitCount = component.DigitFlags is not null
                && component.DigitFlags.TryGetValue("Programmable", out bool[] mask) && mask.Length > 0
                    ? mask.Length
                    : MaxDigitCount(component.ProgrammableLampNumbers);

            int maxDigitFromMask = info.Length > 0 ? info.Length : digitCount;
            digitCount = Math.Max(digitCount, maxDigitFromMask);

            byte[] bytes = new byte[digitCount * 8 * 4];
            for (int digit = 0; digit < digitCount; digit++)
            {
                uint[] lamps = component.ProgrammableLampNumbers.TryGetValue(digit, out uint[] block)
                    ? block
                    : null;
                for (int lamp = 0; lamp < 8; lamp++)
                {
                    uint value = lamps is not null && lamp < lamps.Length ? lamps[lamp] : 0xFFFFFFFF;
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((digit * 8 + lamp) * 4, 4), value);
                }
            }

            return bytes;
        }

        private static int MaxDigitCount(IReadOnlyDictionary<int, uint[]> programmable)
        {
            int max = 0;
            foreach (int digit in programmable.Keys)
            {
                max = Math.Max(max, digit + 1);
            }

            return max;
        }

        private static bool TryReverseStyle(IReadOnlyDictionary<string, string> strings, out byte styleId)
        {
            styleId = 0;
            return strings.TryGetValue("SelectedType", out string styleName)
                && SevenSegStyleIdsByDisplayName.TryGetValue(styleName, out styleId);
        }

        private static Dictionary<string, byte> Invert(Dictionary<byte, string> map)
        {
            var inverted = new Dictionary<string, byte>(StringComparer.Ordinal);
            foreach (var kvp in map)
            {
                inverted[kvp.Value] = kvp.Key;
            }

            return inverted;
        }
    }
}
