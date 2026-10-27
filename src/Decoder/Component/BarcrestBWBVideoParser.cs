using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Decoder.Component.Core;
using MfmeFmlDecoder.src.Decoder.Component.Profiles;
using MfmeFmlDecoder.src.Model.Component;
using System;

namespace MfmeFmlDecoder.src.Decoder.Component
{
    internal sealed class BarcrestBWBVideoParser : ComponentParserBase<BarcrestBWBVideo>
    {

        public BarcrestBWBVideo Parse(ParsingContext context, long componentOffset, uint componentId, byte[] data)
        {
            ComponentProfile profile = ComponentProfileCatalog.Resolve(MFMEComponentType.BarcrestBWBVideo, context.Version);
            // Normalize for MFME angle bug
            var (component, offset, parseData) = ParseBase(context, profile, componentOffset, componentId, data);

            DumpRemaining(componentOffset, data, offset);

                        var parseResult = ParseExtendedTags(component, profile, data, offset);

            return component;
        }
    }
}
