using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Decoder.Component.Core;
using MfmeFmlDecoder.src.Decoder.Component.Profiles;
using MfmeFmlDecoder.src.Decoder.Component.Helper;
using MfmeFmlDecoder.src.Model.Component;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MfmeFmlDecoder.src.Decoder.Component
{
    internal sealed class LampParser : ComponentParserBase<Lamp>
    {

        public Lamp Parse(ParsingContext context, long componentOffset, uint componentId, byte[] data)
        {
            ComponentProfile profile = ComponentProfileCatalog.Resolve(MFMEComponentType.Lamp, context.Version);
            // Normalize for MFME angle bug
            var (component, offset, parseData) = ParseBase(context, profile, componentOffset, componentId, data);

                        var parseResult = ParseExtendedTags(component, profile, parseData, offset);
            ApplyExtendedTags(component, parseResult.ValuesByTag);

            return component;
        }

        private static void ApplyExtendedTags(
            Lamp component,
            IReadOnlyDictionary<uint, object> valuesByTag)
        {
            if ((valuesByTag.TryGetValue(0x49, out var selectedCoinNoteId)) && (valuesByTag.TryGetValue(0x48, out var selectedEffectId)))
            {
                component.Strings.Add("SelectedCoinNote", CoinNoteHierarchy.ResolveCoinNote((int)selectedCoinNoteId, (uint)selectedEffectId));
                component.Strings.Add("SelectedEffect", CoinNoteHierarchy.ResolveEffect((int)selectedCoinNoteId, (uint)selectedEffectId));
            }
            component.Int32s.Remove("SelectedCoinNoteId");
            component.UInt32s.Remove("SelectedEffectId");

            if (valuesByTag.TryGetValue(0x39, out var sublampTableValue)
                && sublampTableValue is IReadOnlyList<LampSublampTableEntry> sublampTable)
            {
                component.SublampTable = sublampTable;
            }
        }

    }
}
