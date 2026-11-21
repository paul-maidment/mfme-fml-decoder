using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Decoder.Component.Profiles;
using MfmeFmlDecoder.src.Model.Component;
using MfmeFmlDecoder.Utilities;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>
    /// Rebuilds a single component record payload (after the 8-byte record header) from the
    /// decoded model: preamble + geometry + extended TLV sections + terminator. The reader
    /// ignores the preamble for single-stream/legacy layouts, so a zero preamble is emitted.
    /// </summary>
    internal static class ComponentPayloadWriter
    {
        // MFME 20.1 writes a small, kind-specific base preamble immediately after the angle
        // (whose length is the profile's ValidAngleOffsetDelta). These are the only kinds whose
        // preamble bytes are not modelled tags. The Flag is the default value for tag 0x07
        // (element visibility): 0x01 = shown, 0x00 = hidden (MFME writes Checkbox hidden).
        private static readonly Dictionary<MFMEComponentType, (byte Flag, byte[] Preamble)> GeometryQuirks = new()
        {
            [MFMEComponentType.BFMLED] = (0x01, new byte[] { 0x0A, 0x00 }),
            [MFMEComponentType.BFMColourLed] = (0x01, new byte[] { 0x0A, 0x00 }),
            [MFMEComponentType.EpochMatrix] = (0x01, new byte[] { 0x0A, 0x00 }),
            [MFMEComponentType.BFMVideo] = (0x01, new byte[] { 0x0B, 0x00 }),
            [MFMEComponentType.DiscReel] = (0x01, new byte[] { 0x0B, 0x00 }),
            [MFMEComponentType.BarcrestBWBVideo] = (0x01, new byte[] { 0x0B, 0x00 }),
            [MFMEComponentType.MaygayVideo] = (0x01, new byte[] { 0x0B, 0x00 }),
            [MFMEComponentType.PlasmaDisplay] = (0x01, new byte[] { 0x0A, 0x00, 0x0B, 0x00 }),
            [MFMEComponentType.SevenSegBlock] = (0x01, new byte[] { 0x0A, 0x00, 0x0B, 0x00 }),
            [MFMEComponentType.ReelBonusReel] = (0x01, new byte[] { 0x0A, 0x00, 0x0B, 0x00 }),
            [MFMEComponentType.Led] = (0x01, new byte[] { 0x1C, 0x01, 0x00, 0x00, 0x00 }),
            [MFMEComponentType.AlphaNew] = (0x01, new byte[] { 0x3B, 0x00, 0x00, 0x00, 0x00 }),
            [MFMEComponentType.Checkbox] = (0x00, new byte[] { 0x17, 0x01, 0x18, 0x00, 0x00, 0x00, 0x00 }),
        };

        // Class-section tags that must never be written as scalar fields. Lamp declares 0x68 and
        // 0x08 as "Unknown" UINT32 scalars in its class map, but 0x68 is really a nested TLV block
        // and 0x08 collides with the base angle tag; emitting either as an extra class field
        // corrupts MFME's parse (verified: force-emitting them diverges ~46% of the frame).
        private static readonly Dictionary<MFMEComponentType, IReadOnlySet<uint>> NeverEmitClassTags = new()
        {
            [MFMEComponentType.Lamp] = new HashSet<uint> { 0x68, 0x08 },
        };

        // Kinds whose on-disk record is a base section then a class section, separated by the
        // 4C <orientation> 00 marker. The value is the highest tag id in the class section;
        // tags above it stay in the base section. (Their low-tag class fields would otherwise
        // be read as geometry by the reader, and 0x08 collides with the angle.)
        private static readonly Dictionary<MFMEComponentType, uint> ClassSectionMaxTag = new()
        {
            [MFMEComponentType.Button] = 0x0F,
            [MFMEComponentType.Bitmap] = 0x08,
            [MFMEComponentType.MatrixAlpha] = 0x08,
            [MFMEComponentType.Led] = 0x07,
            [MFMEComponentType.Frame] = 0x02,
            [MFMEComponentType.DotMatrix] = 0x04,
            [MFMEComponentType.AceMatrix] = 0x06,
            [MFMEComponentType.AlphaNew] = 0x0F,
            [MFMEComponentType.Alpha] = 0x08,
            [MFMEComponentType.DiscReel] = 0x11,
            // Remaining single-list kinds: class loader owns the low range (0x01..0x0F);
            // higher tags are base fields. (Kinds with a nested tag map are already split.)
            [MFMEComponentType.AceVideo] = 0x0F,
            [MFMEComponentType.AstraMatrix] = 0x0F,
            [MFMEComponentType.BFMAlpha] = 0x0F,
            [MFMEComponentType.BFMColourLed] = 0x0F,
            [MFMEComponentType.BFMLED] = 0x0F,
            [MFMEComponentType.BFMVideo] = 0x0F,
            [MFMEComponentType.BarcrestBWBVideo] = 0x0F,
            [MFMEComponentType.Checkbox] = 0x0F,
            [MFMEComponentType.CoinmasterVideo] = 0x0F,
            [MFMEComponentType.DotAlpha] = 0x0F,
            [MFMEComponentType.EpochDotAlpha] = 0x0F,
            [MFMEComponentType.EpochMatrix] = 0x0F,
            [MFMEComponentType.FlipReel] = 0x0F,
            [MFMEComponentType.IGTVfd] = 0x0F,
            [MFMEComponentType.Label] = 0x0F,
            [MFMEComponentType.MaygayMatrix] = 0x0F,
            [MFMEComponentType.MaygayVideo] = 0x0F,
            [MFMEComponentType.PlasmaDisplay] = 0x0F,
            [MFMEComponentType.ProconnMatrix] = 0x0F,
            [MFMEComponentType.PrismLamp] = 0x0F,
            [MFMEComponentType.ReelBonusReel] = 0x0F,
            [MFMEComponentType.RGBLed] = 0x0F,
        };

        // MFME serialises BandReel tags in a fixed class-field order, and its reader is
        // order-sensitive for this kind (a reordered stream loads as "no layout"). This is the
        // observed 20.1 order; absent tags are simply skipped.
        private static readonly int[] BandReelTagOrder =
        {
            0x1C, 0x36, 0x38, 0x39, 0x3B, 0x3C, 0x40, 0x4C, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
            0x09, 0x0C, 0x0D, 0x0E, 0x10, 0x14, 0x17, 0x18, 0x19, 0x01, 0x08, 0x13, 0x16,
        };

        public static byte[] WritePayload(BaseComponent component, MfmeVersion version, bool valueBasedSparse = false, bool emitAllModelled = false)
        {
            MFMEComponentType kind = ComponentKind.Resolve(component);
            NeverEmitClassTags.TryGetValue(kind, out IReadOnlySet<uint> skipClassTags);
            ComponentProfile profile = ComponentProfileCatalog.Resolve(kind, version);

            ComponentSpecialValues specials = ComponentSpecialValues.Create(component);
            ComponentTagMap classTags = profile.EffectiveClassTags;
            ComponentTagMap nestedTags = classTags?.NestedTagBlockMap;

            using var payload = new MemoryStream();
            bool legacyTwoSection = profile.BaseSection?.Mode == BaseSectionMode.LegacyTwoSection;
            bool legacyAnsiText = profile.Options?.LegacyAnsiText == true;
            var emittedNames = new HashSet<string>(StringComparer.Ordinal);
            WriteGeometry(payload, component, kind, omitZeroAngle: legacyTwoSection);

            if (legacyTwoSection && nestedTags is not null)
            {
                // MFME legacy two-section layout: base section, a plain 0x00 terminator,
                // then the own/class section, then 0x00. There is no 4C opener — that byte
                // is the V20 nested-block marker and the legacy reader would swallow the
                // second section into the first.
                ComponentTagMap firstTags = profile.EffectiveBaseTags;
                ComponentTagMap secondTags = profile.BaseSection.SecondSectionTags ?? firstTags;
                WriteScope(payload, component, firstTags, specials, rawValues: component.RawTagValues, ascending: true, legacyAnsiText: legacyAnsiText, onlyRawPresent: !emitAllModelled, valueBasedSparse: valueBasedSparse);
                WireWriter.U8(payload, 0x00);
                WriteScope(payload, component, secondTags, specials, rawValues: component.RawTagValues, ascending: true, legacyAnsiText: legacyAnsiText, onlyRawPresent: !emitAllModelled, valueBasedSparse: valueBasedSparse);
                WireWriter.U8(payload, 0x00);
                return payload.ToArray();
            }

            if (ClassSectionMaxTag.TryGetValue(kind, out uint classMaxTag))
            {
                // Base section (high tags), then the 4C <orientation> 00 marker, then the
                // class section (low tags: colours / images / shape / offsets) in ascending
                // order to match MFME.
                WriteScope(payload, component, classTags, specials, include: key => !InClassSection(kind, key, classMaxTag), rawValues: component.RawTagValues, legacyAnsiText: legacyAnsiText, onlyRawPresent: !emitAllModelled, valueBasedSparse: valueBasedSparse, skipTags: skipClassTags);
                WireWriter.U8(payload, 0x4C);
                WireWriter.U8(payload, OrientationByte(component.Orientation));
                WireWriter.U8(payload, 0x00);
                WriteScope(payload, component, classTags, specials, include: key => InClassSection(kind, key, classMaxTag), rawValues: component.RawTagValues, ascending: true, legacyAnsiText: legacyAnsiText, onlyRawPresent: !emitAllModelled, valueBasedSparse: valueBasedSparse, skipTags: skipClassTags);
                WireWriter.U8(payload, 0x00);
                return payload.ToArray();
            }

            WriteScope(payload, component, classTags, specials, rawValues: component.RawTagValues, legacyAnsiText: legacyAnsiText, onlyRawPresent: !emitAllModelled, emittedNames: emittedNames, valueBasedSparse: valueBasedSparse, skipAngleTag: kind == MFMEComponentType.Reel,
                explicitOrder: kind == MFMEComponentType.BandReel ? BandReelTagOrder : null, skipTags: skipClassTags);

            if (nestedTags is not null)
            {
                // WriteNestedBlock already writes the component's closing 0x00 terminator, so do
                // not add another here (MFME writes the nested block then a single terminator).
                // SevenSeg/SevenSegBlock cross-version: MFME emits the whole nested set regardless
                // of default values, and omitting a default-valued flag (e.g. a DP flag) can render
                // subtly differently, so emit every modelled tag. Same-version stays presence-based.
                bool nestedEmitAll = emitAllModelled
                    || (valueBasedSparse && (kind == MFMEComponentType.SevenSeg || kind == MFMEComponentType.SevenSegBlock));
                WriteNestedBlock(payload, component, nestedTags, specials, legacyAnsiText, emittedNames, onlyRawPresent: !nestedEmitAll, valueBasedSparse: valueBasedSparse);
                return payload.ToArray();
            }

            // BandReel's 0x4C (HorizontalVertical) is written in-order by the scope above, so the
            // bare orientation opener must not be emitted for it.
            if (component.Orientation is not null && kind != MFMEComponentType.BandReel)
            {
                // A bare 4C ?? 00 opener still carries the orientation byte for components
                // that have no nested tag map (the reader consumes three bytes and continues).
                WriteBareOrientationOpener(payload, component);
            }

            WireWriter.U8(payload, 0x00);
            return payload.ToArray();
        }

        // MFME on-disk geometry = pre-list tag 0x3D (4-byte value), tags 0x01..0x05
        // (u8 tag + u32), then base-list tags 0x07 (1-byte flag) and, only for a
        // non-zero angle, 0x08 (int32 half-degree steps). There is no geometry
        // terminator; the angle bytes are part of the base list.
        private static void WriteGeometry(Stream stream, BaseComponent component, MFMEComponentType kind, bool omitZeroAngle)
        {
            WireWriter.U8(stream, 0x3D);
            WireWriter.U32(stream, 0);
            WireWriter.U8(stream, 0x01);
            WireWriter.I32(stream, component.X);
            WireWriter.U8(stream, 0x02);
            WireWriter.I32(stream, component.Y);
            WireWriter.U8(stream, 0x03);
            WireWriter.U32(stream, component.Height);
            WireWriter.U8(stream, 0x04);
            WireWriter.U32(stream, component.Width);
            WireWriter.U8(stream, 0x05);
            WireWriter.I32(stream, component.Number);

            byte flag = 0x01;
            byte[] preamble = null;
            if (GeometryQuirks.TryGetValue(kind, out (byte Flag, byte[] Preamble) quirk))
            {
                flag = quirk.Flag;
                preamble = quirk.Preamble;
            }

            // Tag 0x07 is element visibility (MFME stores it at obj+0xFC; 0x01 = shown, 0x00 = hidden).
            if (component.Visible is bool visible)
            {
                flag = visible ? (byte)0x01 : (byte)0x00;
            }

            // Always emit a valid angle wire (0x07 flag byte + 0x08 int32 half-degree steps),
            // including angle 0, so the reader takes its valid-angle path. The kind preamble
            // then fills the profile's ValidAngleOffsetDelta so the extended section aligns.
            double angle = QuantizeAngle(component.Angle);
            WireWriter.U8(stream, 0x07);
            WireWriter.U8(stream, flag);
            if (angle != 0.0)
            {
                WireWriter.U8(stream, 0x08);
                WireWriter.I32(stream, (int)Math.Round(angle * 2.0, MidpointRounding.AwayFromZero));
            }

            // Led/AlphaNew's rewrite-path prelude is skipped only when the angle is 0; at a
            // non-zero angle those bytes are a real captured tag, so don't double-emit.
            if (preamble is not null && !((kind == MFMEComponentType.Led || kind == MFMEComponentType.AlphaNew) && angle != 0.0))
            {
                WireWriter.Bytes(stream, preamble);
            }
        }

        private static double QuantizeAngle(float angle)
        {
            return Math.Round(angle * 2.0, MidpointRounding.AwayFromZero) / 2.0;
        }

        private static void WriteScope(
            Stream stream,
            BaseComponent component,
            ComponentTagMap map,
            ComponentSpecialValues specials,
            Func<uint, bool> include = null,
            Dictionary<uint, byte[]> rawValues = null,
            bool ascending = false,
            bool legacyAnsiText = false,
            HashSet<string> emittedNames = null,
            bool onlyRawPresent = false,
            bool valueBasedSparse = false,
            bool skipAngleTag = false,
            IReadOnlyList<int> explicitOrder = null,
            IReadOnlySet<uint> skipTags = null)
        {
            if (map is null)
            {
                return;
            }

            emittedNames ??= new HashSet<string>(StringComparer.Ordinal);
            bool lampTableEmitted = false;
            // The geometry reader treats any tag <= 0x07 as another geometry field, so the
            // first extended tag must be > 0x07 (this matches what MFME writes). Emit the
            // > 0x07 tags first, then the low tags (0x01..0x07), each group ascending. Inside a
            // class section (after the 4C marker) emit plain ascending order to match MFME.
            IEnumerable<KeyValuePair<uint, TagInfo>> ordered;
            if (explicitOrder is not null)
            {
                ordered = map.OrderBy(kvp =>
                {
                    int i = 0;
                    foreach (int t in explicitOrder) { if (t == (int)kvp.Key) return i; i++; }
                    return int.MaxValue;
                }).ThenBy(kvp => kvp.Key);
            }
            else
            {
                ordered = ascending
                    ? map.OrderBy(kvp => kvp.Key)
                    : map.OrderBy(kvp => kvp.Key > 0x07 ? 0 : 1).ThenBy(kvp => kvp.Key);
            }
            foreach (KeyValuePair<uint, TagInfo> entry in ordered)
            {
                TagInfo info = entry.Value;
                if (info is null || info.Role == ValueRole.NESTED_TAG_BLOCK)
                {
                    continue;
                }

                // Restrict a scope to a tag range (used to split a Button base/class section).
                if (include is not null && !include(entry.Key))
                {
                    continue;
                }

                                // Reel's base map carries a stray 0x08 ("Unknown") that collides with the geometry
                // angle (0x08); WriteGeometry already emits the angle. Skip it in the base scope
                // only (the nested 0x08 is the real "Resolution" field).
                if (skipAngleTag && entry.Key == 0x08)
                {
                    continue;
                }

                // Per-kind class tags that must never be emitted (see NeverEmitClassTags).
                if (skipTags is not null && skipTags.Contains(entry.Key))
                {
                    continue;
                }

                // Opaque tags (Unknown*/RAW) are not in the typed model; re-emit the captured raw
                // bytes so rendering-relevant tags survive the round-trip.
                if (IsOpaqueTag(info))
                {
                    bool emittedOpaque = false;
                    if (rawValues is not null
                        && rawValues.TryGetValue(entry.Key, out byte[] raw)
                        && raw is { Length: > 0 }
                        // Same-version: echo verbatim. Cross-version: genuine RAW blobs (unknown
                        // layout, no fixed length in the profile) are echoed as-is; typed
                        // "Unknown" tags are echoed only if the captured length already equals
                        // the 20.1 tag length (otherwise they would misalign the component).
                        && (!valueBasedSparse || info.Role == ValueRole.RAW || raw.Length == info.Length))
                    {
                        WireWriter.U8(stream, (byte)entry.Key);
                        WireWriter.Bytes(stream, raw);
                        emittedOpaque = true;
                    }

                    // If no raw was captured but the model can rebuild the tag (e.g. Border's
                    // BoundingBox/PaneSplit RAW blobs derived from BoxX/LeftWidth/... fields),
                    // reconstruct it from the model instead of dropping it.
                    if (!emittedOpaque
                        && TagValueWriter.TryEncode(component, info, specials, out byte[] opaqueDerived, legacyAnsiText)
                        && opaqueDerived is { Length: > 0 })
                    {
                        WireWriter.U8(stream, (byte)entry.Key);
                        WireWriter.Bytes(stream, opaqueDerived);
                    }
                    continue;
                }

                // Some maps alias two tag ids to the same attribute name (e.g. Button label
                // 0x28/0x3F). The decoder rejects the duplicate, so emit only the first
                // (lowest tag id) occurrence of each attribute name per component.
                string attributeName = info.AttributeName;
                if (emittedNames is not null && !string.IsNullOrEmpty(attributeName)
                    && !emittedNames.Add(attributeName))
                {
                    continue;
                }                // Sparse 20.1 output: emit a tag when it was present in the source, or
                // when the model value differs from the tag's default (so cross-version writes
                // reconstruct what MFME would emit, independent of the source tag layout).
                bool present = rawValues is not null && rawValues.ContainsKey(entry.Key);

                // Only the first LAMP_SUBLAMP_TABLE tag is written (some maps carry two aliases).
                if (info.Role == ValueRole.LAMP_SUBLAMP_TABLE)
                {
                    if (lampTableEmitted)
                    {
                        continue;
                    }

                    if (TagValueWriter.TryEncode(component, info, specials, out byte[] tableBytes, legacyAnsiText)
                        && (!onlyRawPresent || present || (valueBasedSparse && !(info.DefaultValues is { Length: > 0 } && BytesEqual(tableBytes, info.DefaultValues)))))
                    {
                        WireWriter.U8(stream, (byte)entry.Key);
                        WireWriter.Bytes(stream, tableBytes);
                        lampTableEmitted = true;
                    }
                    continue;
                }

                if (TagValueWriter.TryEncode(component, info, specials, out byte[] bytes, legacyAnsiText)
                    && (!onlyRawPresent || present || (valueBasedSparse && !(info.DefaultValues is { Length: > 0 } && BytesEqual(bytes, info.DefaultValues)))))
                {
                    WireWriter.U8(stream, (byte)entry.Key);
                    WireWriter.Bytes(stream, bytes);
                }
                else if (explicitOrder is not null
                    && rawValues is not null
                    && rawValues.TryGetValue(entry.Key, out byte[] rawFallback)
                    && rawFallback is { Length: > 0 })
                {
                    // BandReel: 0x4C (HorizontalVertical) and similar present-in-source tags whose
                    // typed encoder cannot rebuild them are echoed from the captured raw bytes so
                    // MFME's fixed-order, presence-based stream stays faithful.
                    WireWriter.U8(stream, (byte)entry.Key);
                    WireWriter.Bytes(stream, rawFallback);
                }
                else if (explicitOrder is not null && entry.Key == 0x4C)
                {
                    // Legacy BandReel has no 0x4C tag; MFME's 20.1 conversion still writes the
                    // HorizontalVertical field (in its fixed position) from the component's
                    // orientation. 0 = vertical, 1 = horizontal.
                    WireWriter.U8(stream, 0x4C);
                    WireWriter.U16(stream, component.Orientation == "Horizontal" ? (ushort)1 : (ushort)0);
                }
            }
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a is null || b is null)
            {
                return false;
            }

            // Compare numerically: profile defaults are often declared shorter than the encoded
            // value (e.g. UINT32 default {0x02} vs 4 encoded bytes). Zero-pad to the longer length
            // so a default-valued tag is actually recognised as default and left to MFME's own
            // defaults (wrong-sized dot-matrix displays came from always emitting XSize/YSize).
            int n = Math.Max(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                byte x = i < a.Length ? a[i] : (byte)0x00;
                byte y = i < b.Length ? b[i] : (byte)0x00;
                if (x != y)
                {
                    return false;
                }
            }

            return true;
        }

        // 4C <orientation> 00  <nested TLV>  00
        private static void WriteNestedBlock(
            Stream stream,
            BaseComponent component,
            ComponentTagMap nestedMap,
            ComponentSpecialValues specials,
            bool legacyAnsiText = false,
            HashSet<string> emittedNames = null,
            bool onlyRawPresent = true,
            bool valueBasedSparse = false,
            bool skipAngleTag = false)
        {
            WireWriter.U8(stream, 0x4C);
            WireWriter.U8(stream, OrientationByte(component.Orientation));
            WireWriter.U8(stream, 0x00);
            // Legacy sources keep some shape/opaque tags (e.g. Lamp 0x0E) at the outer level even
            // though 20.1 writes them inside the 4C block. Fall back to the outer captured raw so
            // those tags are not dropped when emitted in the nested scope.
            var raws = new Dictionary<uint, byte[]>(component.NestedRawTagValues);
            foreach (var kv in component.RawTagValues) { if (!raws.ContainsKey(kv.Key)) raws[kv.Key] = kv.Value; }
            WriteScope(stream, component, nestedMap, specials, rawValues: raws, ascending: true, legacyAnsiText: legacyAnsiText, onlyRawPresent: onlyRawPresent, emittedNames: emittedNames, valueBasedSparse: valueBasedSparse);
            WireWriter.U8(stream, 0x00);
        }

        // 4C <orientation> 00 with no nested payload (orientation-only opener).
        private static void WriteBareOrientationOpener(Stream stream, BaseComponent component)
        {
            WireWriter.U8(stream, 0x4C);
            WireWriter.U8(stream, OrientationByte(component.Orientation));
            WireWriter.U8(stream, 0x00);
        }

        private static bool InClassSection(MFMEComponentType kind, uint tag, uint classMaxTag)
        {
            // DiscReel: class loader owns the low range plus a scattering of high tags
            // (mask images, opto/lamps layout); the rest (0x1C/0x36/0x38/0x39/0x3B/0x3C/0x3E/0x40)
            // are base fields.
            if (kind == MFMEComponentType.DiscReel)
            {
                if (tag <= 0x11)
                {
                    return true;
                }

                switch (tag)
                {
                    case 0x33:
                    case 0x54:
                    case 0x55:
                    case 0x96:
                    case 0x97:
                    case 0x98:
                    case 0x99:
                    case 0x9A:
                    case 0x9B:
                        return true;
                    default:
                        return false;
                }
            }

            if (tag <= classMaxTag)
            {
                return true;
            }

            // AlphaNew's 0x99 (Chop) is a class-scope tag sitting above the low tag range.
            return kind == MFMEComponentType.AlphaNew && tag == 0x99;
        }

        private static bool IsOpaqueTag(TagInfo info)
        {
            if (info is null)
            {
                return false;
            }

            return info.Role == ValueRole.RAW;
        }

        private static byte OrientationByte(string orientation)
        {
            return string.Equals(orientation, "Horizontal", StringComparison.OrdinalIgnoreCase)
                ? (byte)0x01
                : (byte)0x00;
        }
    }
}


















