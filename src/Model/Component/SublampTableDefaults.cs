using System.Collections.Generic;
using MfmeFmlDecoder.Model;

namespace MfmeFmlDecoder.src.Model.Component
{
    /// <summary>
    /// Fixed sub-lamp table sizes per component kind, taken from MFME's component constructors
    /// (callers of <c>FUN_0048bb80(obj, count, fill)</c>): Lamp 12, Reel 15, SevenSeg 16,
    /// SevenSegBlock 16, BandReel 60, Button 2, FlipReel 3. Each ctor allocates the table at this
    /// size, filled with -2 ("undefined"), so a component always has a table even when the source
    /// carries no <c>0x39</c> tag. Our JSON may omit the table; on read that means the empty table
    /// is present and must be materialised at the kind's size.
    /// </summary>
    internal static class SublampTableDefaults
    {
        public static int FixedSlots(MFMEComponentType kind) => kind switch
        {
            MFMEComponentType.Lamp => 12,
            MFMEComponentType.Reel => 15,
            MFMEComponentType.SevenSeg => 16,
            MFMEComponentType.SevenSegBlock => 16,
            MFMEComponentType.BandReel => 60,
            MFMEComponentType.Button => 2,
            MFMEComponentType.FlipReel => 3,
            _ => 0,
        };

        /// <summary>An all-undefined table of the given size (slots 1..N, each -2).</summary>
        public static IReadOnlyList<LampSublampTableEntry> Empty(int slots)
        {
            var table = new List<LampSublampTableEntry>(slots);
            for (int i = 1; i <= slots; i++)
            {
                table.Add(new LampSublampTableEntry(i, -2));
            }

            return table;
        }
    }
}
