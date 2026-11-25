using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MfmeFmlDecoder.Model;

namespace MfmeFmlDecoder.src.Model.Component
{
    internal abstract class BaseComponent
    {
        [JsonIgnore]
        public int X { get; set; }
        [JsonIgnore]
        public int Y { get; set; }
        [JsonIgnore]
        public uint Width { get; set; }
        [JsonIgnore]
        public uint Height { get; set; }
        [JsonIgnore]
        public int Number { get; set; }
        [JsonIgnore]
        public float Angle { get; set; }
        /// <summary>
        /// Element visibility, carried by base tag <c>0x07</c> (MFME stores it at <c>obj+0xFC</c> and
        /// gates repaint / hit-test on it; ctor default = visible). <c>false</c> means hidden.
        /// Null for sources where the byte wasn't a simple 0/1 flag, in which case the writer uses
        /// its per-kind default.
        /// </summary>
        [JsonIgnore]
        internal bool? Visible { get; set; }
        [JsonIgnore]
        public string Orientation { get; set; }

        /// <summary>
        /// Set by <see cref="Layout"/> when emitting the component list; not part of parsed FML data.
        /// </summary>
        [JsonIgnore]
        internal int? OrdinalComponentIdentifier { get; set; }

        public Dictionary<string, FontTagEntry> Fonts { get; } = new Dictionary<string, FontTagEntry>();
        [JsonIgnore]
        public Dictionary<string, string> Strings { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        [JsonIgnore]
        public Dictionary<string, float> Floats { get; } = new Dictionary<string, float>(StringComparer.Ordinal);
        [JsonIgnore]
        public Dictionary<string, uint> UInt32s { get; } = new Dictionary<string, uint>(StringComparer.Ordinal);
        [JsonIgnore]
        public Dictionary<string, int> Int32s { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        [JsonIgnore]
        public Dictionary<string, ushort> UInt16s { get; } = new Dictionary<string, ushort>(StringComparer.Ordinal);
        [JsonIgnore]
        public Dictionary<string, bool> Booleans { get; } = new Dictionary<string, bool>(StringComparer.Ordinal);
        [JsonIgnore]
        public Dictionary<string, byte> Bytes { get; } = new Dictionary<string, byte>(StringComparer.Ordinal);
        [JsonIgnore]
        public Dictionary<string, string> Colours { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        [JsonIgnore]
        public Dictionary<string, BitmapEntry> Images { get; } = new Dictionary<string, BitmapEntry>(StringComparer.Ordinal);

        /// <summary>
        /// Raw bytes of tags the decoder treats as opaque (attribute names starting "Unknown",
        /// or <see cref="ValueRole.RAW"/>). Not part of the JSON model, but re-emitted by the
        /// writer so rendering-relevant tags survive a decode/re-encode round-trip.
        /// </summary>
        [JsonIgnore]
        internal Dictionary<uint, byte[]> RawTagValues { get; } = new Dictionary<uint, byte[]>();

        [JsonIgnore]
        internal Dictionary<uint, byte[]> NestedRawTagValues { get; } = new Dictionary<uint, byte[]>();

        private static readonly JsonSerializerOptions ToJsonOptions = CreateToJsonOptions();

        internal static JsonSerializerOptions CreateJsonWriteOptions(bool writeIndented)
        {
            return new JsonSerializerOptions(ToJsonOptions)
            {
                WriteIndented = writeIndented
            };
        }

        public virtual string ToJson(bool indented = true)
        {
            JsonSerializerOptions options = CreateJsonWriteOptions(indented);
            using MemoryStream stream = new MemoryStream();
            using Utf8JsonWriter writer = new Utf8JsonWriter(
                stream,
                new JsonWriterOptions { Indented = indented });
            WriteTo(writer, options, propertyIndentSpaces: indented ? 2 : 0);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        internal virtual void WriteTo(Utf8JsonWriter writer, JsonSerializerOptions options, int propertyIndentSpaces)
        {
            ToJsonObject().WriteTo(writer, options);
        }

        /// <summary>
        /// Component payload as a JSON object (same shape as <see cref="ToJson"/>), for embedding in a layout document.
        /// </summary>
        public virtual JsonObject ToJsonObject()
        {
            JsonObject json = JsonSerializer.SerializeToNode(this, GetType(), ComponentJsonContext.Default)?.AsObject()
                ?? new JsonObject();
            json.Remove(nameof(Images));
            json.Remove("Bitmaps");
            if (Fonts.Count == 0)
            {
                json.Remove(nameof(Fonts));
            }
            json.Remove(nameof(Strings));
            json.Remove(nameof(Floats));
            json.Remove(nameof(UInt32s));
            json.Remove(nameof(Int32s));
            json.Remove(nameof(UInt16s));
            json.Remove(nameof(Booleans));
            json.Remove(nameof(Bytes));
            json.Remove(nameof(Colours));

            JsonObject values = BuildValuesNode();
            if (values.Count > 0)
            {
                json["Values"] = values;
                foreach (var kvp in values)
                {
                    json.Remove(kvp.Key);
                }
            }

            RemoveDuplicateValueProperties(json, values);

            if (Images.Count > 0)
            {
                json["Images"] = BuildImagesNode();
            }

            JsonObject ordered = new JsonObject
            {
                ["Type"] = GetType().Name,
            };
            if (OrdinalComponentIdentifier.HasValue)
            {
                ordered["OrdinalComponentIdentifier"] = OrdinalComponentIdentifier.Value;
            }

            ordered["Geometry"] = BuildGeometryNode();
            // Tag 0x07 is element visibility (0x01 = shown, 0x00 = hidden). MFME writes Checkbox
            // hidden (0x00) by default; everything else is visible. Carry only the non-default
            // case so the JSON stays clean.
            bool defaultVisible = this is not Checkbox;
            if (Visible is bool visible && visible != defaultVisible)
            {
                ordered["Visible"] = visible;
            }
            foreach (var kvp in json)
            {
                // JsonNodes can only have one parent; clone to reorder safely.
                ordered[kvp.Key] = kvp.Value is null ? null : JsonNode.Parse(kvp.Value.ToJsonString());
            }

            return ordered;
        }

        private static JsonSerializerOptions CreateToJsonOptions()
        {
            return new JsonSerializerOptions(ComponentJsonContext.Default.Options);
        }

        private JsonObject BuildGeometryNode()
        {
            double steppedAngle = Math.Round((double)Angle / 0.5, MidpointRounding.AwayFromZero) * 0.5;
            JsonObject geometry = new JsonObject
            {
                ["X"] = X,
                ["Y"] = Y,
                ["Width"] = Width,
                ["Height"] = Height,
                ["Number"] = Number,
                ["Angle"] = steppedAngle,
            };
            if (Orientation is not null)
            {
                geometry["Orientation"] = Orientation;
            }

            return geometry;
        }

        private JsonObject BuildImagesNode()
        {
            JsonObject images = new JsonObject();

            foreach (var kvp in Images.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                // Placeholders the parser invents (e.g. Button's blank lamp_1_image) are decoder-side
                // conveniences, not source data. Exporting them made the rebuild write a zero-filled
                // bitmap where the original had no tag at all, and MFME crashed on load.
                if (kvp.Value.Synthesized)
                {
                    continue;
                }

                images[kvp.Key] = new JsonObject
                {
                    [nameof(BitmapEntry.Width)] = kvp.Value.Width,
                    [nameof(BitmapEntry.Height)] = kvp.Value.Height,
                    [nameof(BitmapEntry.BitsPerPixel)] = kvp.Value.BitsPerPixel
                };
            }

            return images;
        }

        private JsonObject BuildValuesNode()
        {
            JsonObject values = new JsonObject();
            MergeValues(values, Strings, static v => JsonValue.Create(v));
            MergeValues(values, UInt32s, static v => JsonValue.Create(v));
            MergeValues(values, Int32s, static v => JsonValue.Create(v));
            MergeValues(values, UInt16s, static v => JsonValue.Create(v));
            MergeValues(values, Booleans, static v => JsonValue.Create(v));
            MergeValues(values, Bytes, static v => JsonValue.Create(v));
            MergeValues(values, Floats, static v => JsonValue.Create(v));
            MergeValues(values, Colours, static v => JsonValue.Create(v));
            return values;
        }

        private static void MergeValues<T>(
            JsonObject target,
            IReadOnlyDictionary<string, T> source,
            Func<T, JsonNode> toNode)
        {
            foreach (var kvp in source.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                if (target.ContainsKey(kvp.Key))
                {
                    throw new InvalidOperationException($"Duplicate Values key '{kvp.Key}'.");
                }

                target[kvp.Key] = toNode(kvp.Value);
            }
        }

        /// <summary>
        /// Removes typed properties from the JSON object when they duplicate entries in <see cref="Values"/>
        /// under a different property name (e.g. <c>Reversed</c> vs tag attribute <c>Reverse</c>).
        /// </summary>
        protected virtual void RemoveDuplicateValueProperties(JsonObject json, JsonObject values)
        {
        }
    }
}