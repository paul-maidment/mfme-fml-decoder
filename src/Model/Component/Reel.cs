using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace MfmeFmlDecoder.src.Model.Component
{
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    internal class Reel : BaseComponent
    {
        private const string NonNullSublampCountKey = "NonNullSublampCount";

        [JsonIgnore]
        public IReadOnlyList<LampSublampTableEntry> SublampTable { get; set; } = Array.Empty<LampSublampTableEntry>();

        public string OffColour { get; set; }

        [JsonPropertyName("SubLampNumberTable")]
        [JsonConverter(typeof(SublampTableJsonConverter))]
        public SubLampNumberTablePayload SubLampNumberTableForJson => new()
        {
            NumberOfDefinedLampNumbers = UInt32s.TryGetValue(NonNullSublampCountKey, out uint count)
                ? count
                : null,
            Entries = SublampTable,
        };

        public override JsonObject ToJsonObject()
        {
            JsonObject json = base.ToJsonObject();
            RemoveValueKey(json, NonNullSublampCountKey);
            // NOTE: SelectedOffColourId (tag 0x2A) must stay in Values. It drives which mask/colour
            // MFME uses for the reel's OFF symbols; the friendly OffColour string collapses several
            // ids (0x00 and 0xFF both showed "Mask 1"), so it cannot be used to rebuild the tag.
            return json;
        }

        private static void RemoveValueKey(JsonObject json, string key)
        {
            if (json["Values"] is not JsonObject values || !values.Remove(key))
            {
                return;
            }

            if (values.Count == 0)
            {
                json.Remove("Values");
            }
        }
    }
}
