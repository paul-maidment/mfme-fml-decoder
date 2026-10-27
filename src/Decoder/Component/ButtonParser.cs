using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Model.Component;
using System.Collections.Generic;
using System;
using System.Linq;
using MfmeFmlDecoder.src.Decoder.Component.Core;
using MfmeFmlDecoder.src.Decoder.Component.Profiles;
using MfmeFmlDecoder.src.Decoder.Component.Helper;

namespace MfmeFmlDecoder.src.Decoder.Component
{
    internal sealed class ButtonParser : ComponentParserBase<Button>
    {
        private Dictionary<uint, string> shapeOptions = new Dictionary<uint, string>
        {
            { 0x00, "Rectangle" },
            { 0x01, "Triangle Up" },
            { 0x02, "Triangle Down" },
            { 0x03, "Triangle Left" },
            { 0x04, "Triangle Right" },
            { 0x05, "Ellipse" },
        };


        public Button Parse(ParsingContext context, long componentOffset, uint componentId, byte[] data)
        {
            ComponentProfile profile = ComponentProfileCatalog.Resolve(MFMEComponentType.Button, context.Version);
            // Normalize for MFME angle bug
            var (component, offset, parseData) = ParseBase(context, profile, componentOffset, componentId, data);

            DumpRemaining(componentOffset, parseData, offset);

                        var parseResult = ParseExtendedTags(component, profile, parseData, offset);

            ApplyExtendedTags(component, parseResult.ValuesByTag, shapeOptions);

            return component;
        }

        private static void ApplyExtendedTags(Button component, System.Collections.Generic.IReadOnlyDictionary<uint, object> valuesByTag, Dictionary<uint, string> shapeOptions)
        {
            if ((valuesByTag.TryGetValue(0x49, out var selectedCoinNoteId)) && (valuesByTag.TryGetValue(0x48, out var selectedEffectId)))
            {
                component.Strings.Add("SelectedCoinNote", CoinNoteHierarchy.ResolveCoinNote((int)selectedCoinNoteId, (uint)selectedEffectId));
                component.Strings.Add("SelectedEffect", CoinNoteHierarchy.ResolveEffect((int)selectedCoinNoteId, (uint)selectedEffectId));
            }
            component.Int32s.Remove("SelectedCoinNoteId");
            component.UInt32s.Remove("SelectedEffectId");
            if (valuesByTag.TryGetValue(0x01, out var selectedShapeId)) component.Strings.Add("SelectedShape", shapeOptions[(uint)selectedShapeId]);

            if (valuesByTag.TryGetValue(0x39, out var sublampTableValue)
                && sublampTableValue is IReadOnlyList<LampSublampTableEntry> sublampTable)
            {
                component.SublampTable = sublampTable;
            }
        }
    }
}