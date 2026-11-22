using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MfmeFmlDecoder.src.Model.Component;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>
    /// Values the decoder keeps outside the generic typed dictionaries (they surface as
    /// component properties or as derived dictionaries rather than <c>Values</c>). The encoder
    /// reads them back so the corresponding variable-length tags can be regenerated.
    /// </summary>
    internal sealed class ComponentSpecialValues
    {
        public IReadOnlyList<LampSublampTableEntry> SublampTable { get; private set; }

        public static ComponentSpecialValues Create(BaseComponent component)
        {
            var values = new ComponentSpecialValues();
            values.SublampTable = ResolveSublampTable(component);
            return values;
        }

        private static IReadOnlyList<LampSublampTableEntry> ResolveSublampTable(BaseComponent component)
        {
            // Reel / Lamp / Button / SevenSeg expose the table directly as a property.
            PropertyInfo prop = component.GetType().GetProperty("SublampTable", BindingFlags.Public | BindingFlags.Instance);
            if (prop is not null && typeof(IReadOnlyList<LampSublampTableEntry>).IsAssignableFrom(prop.PropertyType))
            {
                var table = prop.GetValue(component) as IReadOnlyList<LampSublampTableEntry>;
                int fixedSlots = SublampTableDefaults.FixedSlots(ComponentKind.Resolve(component));
                if (table is { Count: > 0 })
                {
                    // MFME 20.1 uses a fixed sub-lamp table size per kind, -2-padded for undefined
                    // slots. Legacy-format sources are smaller (e.g. Reel 10), so pad to the 20.1
                    // size so a re-save matches MFME's own 20.1 conversion.
                    if (fixedSlots > 0 && table.Count < fixedSlots)
                    {
                        var padded = new List<LampSublampTableEntry>(table);
                        for (int i = table.Count; i < fixedSlots; i++)
                        {
                            padded.Add(new LampSublampTableEntry(i + 1, -2));
                        }
                        table = padded;
                    }
                    return table;
                }
                if (fixedSlots > 0)
                {
                    // MFME's component constructor always allocates the sub-lamp table at the kind's
                    // fixed size filled with -2 ("undefined"), so a component with no table tag in
                    // the source still has one and MFME writes it (saver emits 0x38/0x39 when the
                    // count is non-zero; ctors set it via FUN_0048bb80).
                    return SublampTableDefaults.Empty(fixedSlots);
                }
            }

            // RGBLed derives four channel lamp numbers (Int32s, indices 1..4).
            if (component is RGBLed rgbLed)
            {
                return BuildFromInt32s(
                    rgbLed,
                    "RedLampNumber",
                    "GreenLampNumber",
                    "BlueLampNumber",
                    "WhiteLampNumber");
            }

            // FlipReel / PrismLamp hold per-index lamp numbers in UInt32s (keys SubLamp{n}Number).
            var subLamp = BuildFromSubLampNumberKeys(component);
            if (subLamp is not null)
            {
                return subLamp;
            }

            // BandReel rebuilds the wire table from its mask-slot structure.
            if (component is BandReel bandReel)
            {
                return BuildBandReelTable(bandReel);
            }

            // SevenSegBlock stores one lamp number per digit.
            if (component is SevenSegBlock sevenSegBlock && sevenSegBlock.DigitLampNumbers is { Count: > 0 })
            {
                return sevenSegBlock.DigitLampNumbers
                    .Select((n, i) => new LampSublampTableEntry(i + 1, n))
                    .ToList();
            }

            return null;
        }

        private static IReadOnlyList<LampSublampTableEntry> BuildFromInt32s(BaseComponent component, params string[] keys)
        {
            var entries = new List<LampSublampTableEntry>(keys.Length);
            bool any = false;
            int present = 0;
            for (int i = 0; i < keys.Length; i++)
            {
                if (component.Int32s.TryGetValue(keys[i], out int value))
                {
                    entries.Add(new LampSublampTableEntry(i + 1, value));
                    present++;
                    any |= value != 0;
                }
            }

            // Emit the table if any channel is non-zero, or if every channel key is present
            // (a real all-zero RGBLed table; a lone defaulted WhiteLampNumber must not count).
            return (any || present == keys.Length) ? entries : null;
        }

        private static IReadOnlyList<LampSublampTableEntry> BuildFromSubLampNumberKeys(BaseComponent component)
        {
            int maxIndex = 0;
            bool present = false;
            foreach (var kvp in component.UInt32s)
            {
                if (TryParseSubLampKey(kvp.Key, out int index))
                {
                    present = true;
                    if (index > maxIndex) maxIndex = index;
                }
            }

            if (!present)
            {
                return null;
            }

            // MFME's 20.1 Lamp stores a fixed 12-slot sub-lamp table, padding undefined slots
            // with -2, even when the legacy source defined only a few. Mirror that so a legacy
            // re-save matches MFME's own 20.1 conversion.
            int length = Math.Max(component is Lamp ? 12 : 0, maxIndex);
            int[] slots = new int[length];
            for (int i = 0; i < length; i++) slots[i] = -2;
            foreach (var kvp in component.UInt32s)
            {
                if (TryParseSubLampKey(kvp.Key, out int index) && index >= 1 && index <= length)
                {
                    slots[index - 1] = (int)kvp.Value;
                }
            }

            var entries = new List<LampSublampTableEntry>(length);
            for (int i = 0; i < length; i++)
            {
                entries.Add(new LampSublampTableEntry(i + 1, slots[i]));
            }

            return entries;
        }

        private static bool TryParseSubLampKey(string key, out int index)
        {
            index = 0;
            const string prefix = "SubLamp";
            const string suffix = "Number";
            if (key is null || !key.StartsWith(prefix, StringComparison.Ordinal) || !key.EndsWith(suffix, StringComparison.Ordinal))
            {
                return false;
            }

            string middle = key.Substring(prefix.Length, key.Length - prefix.Length - suffix.Length);
            return int.TryParse(middle, out index);
        }

        // Mirror of BandReelParser.SlotToIndexMap: (mask, slot) -> wire table index.
        private static readonly (string Mask, string Slot, int Index)[] BandReelSlotIndex =
        {
            ("mask1", "1a", 0), ("mask1", "1b", 6), ("mask1", "2a", 1), ("mask1", "2b", 7),
            ("mask1", "3a", 2), ("mask1", "3b", 8), ("mask1", "4a", 3), ("mask1", "4b", 9),
            ("mask1", "5a", 4), ("mask1", "5b", 10), ("mask1", "6a", 5), ("mask1", "6b", 11),
            ("mask1", "7a", 24), ("mask1", "7b", 25), ("mask1", "8a", 29), ("mask1", "8b", 29),
            ("mask1", "9a", 33), ("mask1", "9b", 33), ("mask1", "10a", 37), ("mask1", "10b", 38),
            ("mask2", "1a", 12), ("mask2", "1b", 18), ("mask2", "2a", 13), ("mask2", "2b", 19),
            ("mask2", "3a", 14), ("mask2", "3b", 20), ("mask2", "4a", 15), ("mask2", "4b", 21),
            ("mask2", "5a", 16), ("mask2", "5b", 22), ("mask2", "6a", 17), ("mask2", "6b", 23),
            ("mask2", "7a", 26), ("mask2", "7b", 27), ("mask2", "8a", 30), ("mask2", "8b", 31),
            ("mask2", "9a", 34), ("mask2", "9b", 35), ("mask2", "10a", 38), ("mask2", "10b", 39),
            ("mask3", "1a", 40), ("mask3", "1b", 50), ("mask3", "2a", 41), ("mask3", "2b", 51),
            ("mask3", "3a", 42), ("mask3", "3b", 52), ("mask3", "4a", 43), ("mask3", "4b", 53),
            ("mask3", "5a", 44), ("mask3", "5b", 54), ("mask3", "6a", 45), ("mask3", "6b", 55),
            ("mask3", "7a", 46), ("mask3", "7b", 56), ("mask3", "8a", 47), ("mask3", "8b", 57),
            ("mask3", "9a", 48), ("mask3", "9b", 58), ("mask3", "10a", 49), ("mask3", "10b", 59),
        };

        private static IReadOnlyList<LampSublampTableEntry> BuildBandReelTable(BandReel component)
        {
            if (component.MaskSlots?.Masks is null)
            {
                return null;
            }

            int maxIndex = BandReelSlotIndex.Max(s => s.Index);
            int[] table = new int[maxIndex + 1];
            for (int i = 0; i < table.Length; i++)
            {
                table[i] = -2;
            }

            bool any = false;
            foreach (var (mask, slot, index) in BandReelSlotIndex)
            {
                if (component.MaskSlots.Masks.TryGetValue(mask, out var slots)
                    && slots.TryGetValue(slot, out var slotValue))
                {
                    table[index] = slotValue.SublampNumber;
                    any |= slotValue.SublampNumber != 0;
                }
            }

            if (!any)
            {
                return null;
            }

            var entries = new List<LampSublampTableEntry>(table.Length);
            for (int i = 0; i < table.Length; i++)
            {
                entries.Add(new LampSublampTableEntry(i + 1, table[i]));
            }

            return entries;
        }
    }
}
