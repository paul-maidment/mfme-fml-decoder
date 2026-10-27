using MfmeFmlDecoder.Decoder;
using System.Collections.Generic;
using Xunit;

namespace MfmeFmlDecoder.src.Decoder.Component
{
    public class SignedOffsetTests
    {
        // Payload preamble and geometry (X 0x10, Y 0x20, Height 0x28, Width 0x3C, Number 9),
        // then base tag 0x07 = 01 with no angle written, as MFME v20.1 saves a zero angle.
        private static readonly byte[] Geometry =
        {
            0x3D, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x10, 0x00, 0x00, 0x00,
            0x02, 0x20, 0x00, 0x00, 0x00,
            0x03, 0x28, 0x00, 0x00, 0x00,
            0x04, 0x3C, 0x00, 0x00, 0x00,
            0x05, 0x09, 0x00, 0x00, 0x00,
            0x07, 0x01,
        };

        private static byte[] Payload(params byte[] tail)
        {
            var bytes = new List<byte>(Geometry);
            bytes.AddRange(tail);
            return bytes.ToArray();
        }

        [Fact]
        public void Button_TextOffsetsAreSigned()
        {
            byte[] data = Payload(
                0x4C, 0x00, 0x00,
                0x0D, 0xFE, 0xFF, 0xFF, 0xFF,  // XOffset -2
                0x0E, 0xFF, 0xFF, 0xFF, 0xFF,  // YOffset -1
                0x00);

            var button = new ButtonParser().Parse(ParsingContext.V20_1, 0, 5, data);

            Assert.Equal(-2, button.Int32s["XOffset"]);
            Assert.Equal(-1, button.Int32s["YOffset"]);
        }

        [Fact]
        public void Lamp_TextOffsetsAreSigned()
        {
            byte[] data = Payload(
                0x4C, 0x00, 0x00,
                0x21, 0xFB, 0xFF, 0xFF, 0xFF,  // X Offset -5
                0x22, 0xF6, 0xFF, 0xFF, 0xFF,  // Y Offset -10
                0x00);

            var lamp = new LampParser().Parse(ParsingContext.V20_1, 0, 4, data);

            Assert.Equal(-5, lamp.Int32s["X Offset"]);
            Assert.Equal(-10, lamp.Int32s["Y Offset"]);
        }

        [Fact]
        public void Reel_WinlinesOffsetIsSigned()
        {
            byte[] data = Payload(
                0x4C, 0x00, 0x00,
                0x20, 0xFC, 0xFF, 0xFF, 0xFF,  // WinlinesOffset -4
                0x00);

            var reel = new ReelParser().Parse(ParsingContext.V20_1, 0, 3, data);

            Assert.Equal(-4, reel.Int32s["WinlinesOffset"]);
        }
    }
}
