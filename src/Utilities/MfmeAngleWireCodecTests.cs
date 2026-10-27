using System;
using Xunit;

namespace MfmeFmlDecoder.Utilities
{
    public class MfmeAngleWireCodecTests
    {
        [Fact]
        public void IsValidWireAngle_AcceptsSignExtendedNegativeAngle()
        {
            // -10 degrees = -20 half-degree steps = int32 0xFFFFFFEC, as MFME v20 writes it.
            byte[] wire = { 0x01, 0x08, 0xEC, 0xFF, 0xFF, 0xFF };

            Assert.True(MfmeAngleWireCodec.IsValidWireAngle(wire));
            Assert.Equal(-10.0, MfmeAngleWireCodec.BytesToAngle(wire));
        }

        [Fact]
        public void TryReadWireAngle_ReadsSignExtendedNegativeAngle()
        {
            byte[] data = { 0x07, 0x01, 0x08, 0xE3, 0xFF, 0xFF, 0xFF };

            Assert.True(MfmeAngleWireCodec.TryReadWireAngle(data, 1, out double angle));
            Assert.Equal(-14.5, angle);
        }

        [Fact]
        public void IsValidWireAngle_StillAcceptsEncoderForm()
        {
            byte[] wire = MfmeAngleWireCodec.AngleToBytes(-10.0);

            Assert.True(MfmeAngleWireCodec.IsValidWireAngle(wire));
        }

        [Fact]
        public void IsValidWireAngle_PositiveAngleUnchanged()
        {
            byte[] wire = { 0x01, 0x08, 0x5A, 0x00, 0x00, 0x00 };

            Assert.True(MfmeAngleWireCodec.IsValidWireAngle(wire));
            Assert.Equal(45.0, MfmeAngleWireCodec.BytesToAngle(wire));
        }

        [Fact]
        public void IsValidWireAngle_RejectsBytesThatAreNotAnAngle()
        {
            // 07 01 followed by the next base tags (0A 00 0B 00 1C ...), not an angle.
            byte[] wire = { 0x01, 0x0A, 0x00, 0x0B, 0x00, 0x1C };

            Assert.False(MfmeAngleWireCodec.IsValidWireAngle(wire));
        }
    }
}
