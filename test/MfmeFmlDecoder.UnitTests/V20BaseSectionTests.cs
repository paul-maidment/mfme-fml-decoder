using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Decoder.Component;
using MfmeFmlDecoder.src.Model.Component;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MfmeFmlDecoder.UnitTests
{
    public class V20BaseSectionTests
    {
        [Fact]
        public void Button_zero_angle_with_tag_06_before_07_reads_base_and_own_fields()
        {
            var payload = new PayloadBuilder()
                .Geometry(647, 133, 50, 50, 47)
                .Tag(0x06, 0) // MFME 20.1 base tag before the angle
                .ZeroAngle()
                .Byte(0x15, 0x01) // Shortcut 1 enabled
                .Tag(0x16, 48) // Shortcut 1
                .Tag(0x18, 47) // Button Number
                .Orient(0x4C, 0)
                .Colour(0x03, 0xF0, 0xF0, 0xF0, 0xFF) // OffColour (own section)
                .Terminator()
                .Build();

            Button component = new ButtonParser().Parse(0, 0, payload);

            Assert.Equal(47, component.Number);
            Assert.Equal(47u, component.UInt32s["Button Number"]);
            Assert.Equal(48u, component.UInt32s["Shortcut 1"]);
            Assert.True(component.Booleans["Shortcut 1 Enabled"]);
            Assert.Equal("#F0F0F0FF", component.Colours["OffColour"]);
        }

        [Fact]
        public void DiscReel_zero_angle_with_base_0A_0B_reads_outer_dimensions_and_sublamp_table()
        {
            var payload = new PayloadBuilder()
                .Geometry(240, 70, 351, 351, 4)
                .ZeroAngle()
                .BaseByte(0x0A, 0)
                .BaseByte(0x0B, 0)
                .BaseByte(0x0C, 0)
                .Tag(0x1C, 0)
                .Tag(0x38, 12)
                .SublampTable(21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32)
                .Orient(0x4C, 0)
                .Tag(0x0A, 155) // OuterH (own section)
                .Tag(0x0B, 108) // OuterL
                .Tag(0x08, 60) // OuterLampSize
                .Tag(0x11, 60) // InnerLampSize
                .Tag(0x02, 12) // Stops
                .Tag(0x01, 96) // HalfSteps
                .Byte(0x40, 0x00) // Reversed
                .Terminator()
                .Build();

            DiscReel component = new DiscReelParser().Parse(0, 0, payload);

            Assert.Equal(155u, component.OuterH);
            Assert.Equal(108u, component.OuterL);
            Assert.Equal(60u, component.OuterLampSize);
            Assert.Equal(60u, component.InnerLampSize);
            Assert.Equal(12u, component.Stops);
            Assert.Equal(96u, component.HalfSteps);
            Assert.False(component.Reversed);
            Assert.Equal(12, component.SublampTable.Count);
            Assert.Equal(21, component.SublampTable[0].SublampNumber);
            Assert.Equal(32, component.SublampTable[11].SublampNumber);
        }

        [Fact]
        public void AlphaNew_zero_angle_with_overlay_bitmap_reads_base_reversed_and_own_fields()
        {
            var payload = new PayloadBuilder()
                .Geometry(433, 446, 403, 50, 9)
                .ZeroAngle()
                .Bitmap(0x36, CreateMinimalBmp())
                .Tag(0x3B, 0x018942E0)
                .Byte(0x40, 0x01) // Reversed
                .Orient(0x4C, 0)
                .Colour(0x01, 0xFF, 0xFF, 0x00, 0xFF) // OnColour #00FFFFFF
                .Float(0x06, 3.0f) // Thickness
                .Float(0x07, 0.0f) // Spacing
                .Float(0x08, 5.0f) // HorizontalSpacing
                .Float(0x09, 4.0f) // VerticalSpacing
                .Float(0x0A, 5.0f) // Slant
                .Float(0x0B, 48.0f) // Centre
                .Byte(0x0C, 0x01) // Charset
                .Byte(0x0F, 0x01) // Segment16
                .Terminator()
                .Build();

            AlphaNew component = new AlphaNewParser().Parse(0, 0, payload);

            Assert.True(component.Booleans["Reversed"]);
            Assert.True(component.Booleans["Segment16"]);
            Assert.Equal((byte)1, component.Bytes["Charset"]);
            Assert.Equal(AlphaNewCharset.OLD_Charset, component.Charset);
            Assert.Equal(48.0f, component.Floats["Centre"]);
            Assert.Equal(3.0f, component.Floats["Thickness"]);
            Assert.Equal("#00FFFFFF", component.Colours["OnColour"]);
            Assert.Equal(90.0f, component.Floats["Chop"]); // default, tag absent
        }

        [Fact]
        public void AlphaNewCharsetConverter_maps_enum_values_to_MFME_labels()
        {
            var converter = new AlphaNew.AlphaNewCharsetJsonConverter();
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                converter.Write(writer, AlphaNewCharset.OLD_Charset, new JsonSerializerOptions());
            }

            Assert.Equal("\"Old Charset\"", Encoding.UTF8.GetString(stream.ToArray()));
        }

        private static byte[] CreateMinimalBmp()
        {
            byte[] bmp = new byte[58];
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
            BitConverter.GetBytes(54).CopyTo(bmp, 10);
            BitConverter.GetBytes(40).CopyTo(bmp, 14);
            BitConverter.GetBytes(1).CopyTo(bmp, 18);
            BitConverter.GetBytes(1).CopyTo(bmp, 22);
            BitConverter.GetBytes((ushort)1).CopyTo(bmp, 26);
            BitConverter.GetBytes((ushort)32).CopyTo(bmp, 28);
            BitConverter.GetBytes(4).CopyTo(bmp, 34);
            bmp[54] = 0xFF;
            bmp[55] = 0xFF;
            bmp[56] = 0xFF;
            bmp[57] = 0xFF;
            return bmp;
        }

        private sealed class PayloadBuilder
        {
            private readonly List<byte> bytes = new();

            public PayloadBuilder()
            {
                bytes.AddRange(new byte[] { 0x3D, 0x00, 0x00, 0x00, 0x00 });
            }

            public PayloadBuilder Geometry(uint x, uint y, uint height, uint width, int number)
            {
                Tag(0x01, x);
                Tag(0x02, y);
                Tag(0x03, height);
                Tag(0x04, width);
                Tag(0x05, unchecked((uint)number));
                return this;
            }

            public PayloadBuilder ZeroAngle()
            {
                bytes.Add(0x07);
                bytes.Add(0x01);
                return this;
            }

            public PayloadBuilder Byte(byte tag, byte value)
            {
                bytes.Add(tag);
                bytes.Add(value);
                return this;
            }

            public PayloadBuilder BaseByte(byte tag, byte value) => Byte(tag, value);

            public PayloadBuilder Tag(byte tag, uint value)
            {
                bytes.Add(tag);
                bytes.AddRange(BitConverter.GetBytes(value));
                return this;
            }

            public PayloadBuilder Float(byte tag, float value)
            {
                bytes.Add(tag);
                bytes.AddRange(BitConverter.GetBytes(value));
                return this;
            }

            public PayloadBuilder Colour(byte tag, params byte[] value)
            {
                bytes.Add(tag);
                bytes.AddRange(value);
                return this;
            }

            public PayloadBuilder Bitmap(byte tag, byte[] value)
            {
                bytes.Add(tag);
                bytes.AddRange(value);
                return this;
            }

            public PayloadBuilder SublampTable(params int[] lampNumbers)
            {
                bytes.Add(0x39);
                foreach (int lampNumber in lampNumbers)
                {
                    bytes.AddRange(BitConverter.GetBytes(lampNumber));
                }

                return this;
            }

            public PayloadBuilder Orient(byte tag, byte orientation)
            {
                bytes.Add(tag);
                bytes.Add(orientation);
                bytes.Add(0x00);
                return this;
            }

            public PayloadBuilder Terminator()
            {
                bytes.Add(0x00);
                return this;
            }

            public byte[] Build() => bytes.ToArray();
        }
    }
}
