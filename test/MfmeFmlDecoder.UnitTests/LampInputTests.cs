using MfmeFmlDecoder.Decoder;
using MfmeFmlDecoder.src.Decoder.Component;
using MfmeFmlDecoder.src.Model.Component;
using System;
using System.Collections.Generic;
using Xunit;

namespace MfmeFmlDecoder.UnitTests
{
    /// <summary>
    /// Nullable lamp/button base fields. MFME distinguishes "unset" from a stored zero for these, but the
    /// decoder used to inject a zero default and collapse the two:
    ///   * 0x18 Button Number  - written iff the paired 0x17 flag (obj+0xEC) is set; blank means "no input".
    ///   * 0x4A Inhibit Lamp   - written iff the value is >= 0 (ctor default -1); blank means "no inhibit".
    /// A layout with the tag absent and one with the tag set to 0 are therefore different files and must
    /// decode to different output.
    /// </summary>
    public class LampInputTests
    {
        [Fact]
        public void Lamp_without_input_omits_button_number_but_input_zero_keeps_it()
        {
            Lamp noInput = new LampParser().Parse(ParsingContext.V20_1, 0, 0, BuildLamp(buttonNumber: null));
            Lamp inputZero = new LampParser().Parse(ParsingContext.V20_1, 0, 0, BuildLamp(buttonNumber: 0));

            Assert.False(
                noInput.UInt32s.ContainsKey("ButtonNumber"),
                "A lamp with no input (tag 0x18 absent) must not report a ButtonNumber.");
            Assert.True(
                inputZero.UInt32s.ContainsKey("ButtonNumber"),
                "A lamp explicitly wired to input 0 (tag 0x18 == 0) must report ButtonNumber 0.");
            Assert.Equal(0u, inputZero.UInt32s["ButtonNumber"]);
        }

        [Fact]
        public void Button_without_input_omits_button_number_but_input_zero_keeps_it()
        {
            Button noInput = new ButtonParser().Parse(ParsingContext.V20_1, 0, 0, BuildButton(buttonNumber: null));
            Button inputZero = new ButtonParser().Parse(ParsingContext.V20_1, 0, 0, BuildButton(buttonNumber: 0));

            Assert.False(
                noInput.UInt32s.ContainsKey("Button Number"),
                "A button with no input (tag 0x18 absent) must not report a Button Number.");
            Assert.True(
                inputZero.UInt32s.ContainsKey("Button Number"),
                "A button explicitly wired to input 0 (tag 0x18 == 0) must report Button Number 0.");
            Assert.Equal(0u, inputZero.UInt32s["Button Number"]);
        }

        [Fact]
        public void Lamp_without_inhibit_omits_it_but_inhibit_zero_keeps_it()
        {
            Lamp noInhibit = new LampParser().Parse(ParsingContext.V20_1, 0, 0, BuildLamp(inhibitLampNumber: null));
            Lamp inhibitZero = new LampParser().Parse(ParsingContext.V20_1, 0, 0, BuildLamp(inhibitLampNumber: 0));

            Assert.False(
                noInhibit.UInt32s.ContainsKey("InhibitLampNumber"),
                "A lamp with no inhibit (tag 0x4A absent) must not report an InhibitLampNumber.");
            Assert.True(
                inhibitZero.UInt32s.ContainsKey("InhibitLampNumber"),
                "A lamp explicitly inhibited by lamp 0 (tag 0x4A == 0) must report InhibitLampNumber 0.");
            Assert.Equal(0u, inhibitZero.UInt32s["InhibitLampNumber"]);
        }

        [Fact]
        public void Button_without_inhibit_omits_it_but_inhibit_zero_keeps_it()
        {
            Button noInhibit = new ButtonParser().Parse(ParsingContext.V20_1, 0, 0, BuildButton(inhibitLampNumber: null));
            Button inhibitZero = new ButtonParser().Parse(ParsingContext.V20_1, 0, 0, BuildButton(inhibitLampNumber: 0));

            Assert.False(
                noInhibit.UInt32s.ContainsKey("InhibitLamp"),
                "A button with no inhibit (tag 0x4A absent) must not report an InhibitLamp.");
            Assert.True(
                inhibitZero.UInt32s.ContainsKey("InhibitLamp"),
                "A button explicitly inhibited by lamp 0 (tag 0x4A == 0) must report InhibitLamp 0.");
            Assert.Equal(0u, inhibitZero.UInt32s["InhibitLamp"]);
        }

        private static byte[] BuildLamp(uint? buttonNumber = null, uint? inhibitLampNumber = null)
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x3D, 0x00, 0x00, 0x00, 0x00 }); // pre-list tag 0x3d
            AddGeometry(bytes);
            if (buttonNumber.HasValue)
            {
                AddTag(bytes, 0x18, buttonNumber.Value);
            }

            if (inhibitLampNumber.HasValue)
            {
                AddTag(bytes, 0x4A, inhibitLampNumber.Value);
            }

            bytes.Add(0x00); // extended-section terminator
            return bytes.ToArray();
        }

        private static byte[] BuildButton(uint? buttonNumber = null, uint? inhibitLampNumber = null)
        {
            var bytes = new List<byte>();
            bytes.AddRange(new byte[] { 0x3D, 0x00, 0x00, 0x00, 0x00 }); // pre-list tag 0x3d
            AddGeometry(bytes);
            if (buttonNumber.HasValue)
            {
                AddTag(bytes, 0x18, buttonNumber.Value);
            }

            if (inhibitLampNumber.HasValue)
            {
                AddTag(bytes, 0x4A, inhibitLampNumber.Value);
            }

            bytes.Add(0x00); // extended-section terminator
            return bytes.ToArray();
        }

        private static void AddGeometry(List<byte> bytes)
        {
            AddTag(bytes, 0x01, 100); // X
            AddTag(bytes, 0x02, 100); // Y
            AddTag(bytes, 0x03, 17);  // Height
            AddTag(bytes, 0x04, 17);  // Width
            AddTag(bytes, 0x05, 1);   // Number
            bytes.Add(0x07);          // zero angle (6-byte MFME wire: 01 08 + int16 half-degrees + sign ext)
            bytes.AddRange(new byte[] { 0x01, 0x08, 0x00, 0x00, 0x00, 0x00 });
        }

        private static void AddTag(List<byte> bytes, byte tag, uint value)
        {
            bytes.Add(tag);
            bytes.AddRange(BitConverter.GetBytes(value));
        }
    }
}
