// GameConfig map for ACE1.
// Key set derived from every "System ACE1" .gam in the archive (14 files):
//   System, DIP 1, DIP 2, Delay, SetPercent, TotalIn, TotalOut, Played,
//   Layout, Game, ROM  (+ occasional REEL n, Throttle, Effects, Version, DX)
// All values are stored as GAM text lines; the layout side only mirrors the
// description (Caption) as the other ACE-family maps do.

namespace MfmeFmlDecoder.GameConfig.Maps
{
    using MfmeFmlDecoder.GameConfig;

    internal static class ACE1GameConfigMap
    {
        public const string SystemName = "ACE1";

        private const string Json =
            @"{
              ""system"": ""ACE1"",
              ""controls"": {
                ""Caption"": {
                  ""kind"": ""text"",
                  ""storage"": ""fml"",
                  ""tag"": ""0x0C"",
                  ""encoding"": ""ascii-null-padded"",
                  ""readOnly"": true,
                  ""note"": ""Game Details Caption is read-only; mirrors layout Description tag 0x0C.""
                },
                ""DIP"": {
                  ""kind"": ""dip-banks"",
                  ""storage"": ""gam"",
                  ""banks"": {
                    ""DIP1"": {
                      ""gamLine"": ""DIP 1"",
                      ""bits"": 8
                    },
                    ""DIP2"": {
                      ""gamLine"": ""DIP 2"",
                      ""bits"": 8
                    }
                  },
                  ""stringLayout"": {
                    ""order"": ""unknown"",
                    ""index0"": ""UI switch 1"",
                    ""index7"": ""UI switch 8"",
                    ""on"": ""1"",
                    ""off"": ""0""
                  }
                },
                ""Delay"": {
                  ""kind"": ""number"",
                  ""storage"": ""gam"",
                  ""encoding"": ""integer"",
                  ""gamLine"": ""Delay <value>""
                },
                ""SetPercent"": {
                  ""kind"": ""number"",
                  ""storage"": ""gam"",
                  ""encoding"": ""integer"",
                  ""gamLine"": ""SetPercent <value>""
                },
                ""TotalIn"": {
                  ""kind"": ""number"",
                  ""storage"": ""gam"",
                  ""encoding"": ""integer"",
                  ""gamLine"": ""TotalIn <value>""
                },
                ""TotalOut"": {
                  ""kind"": ""number"",
                  ""storage"": ""gam"",
                  ""encoding"": ""integer"",
                  ""gamLine"": ""TotalOut <value>""
                },
                ""Played"": {
                  ""kind"": ""number"",
                  ""storage"": ""gam"",
                  ""encoding"": ""integer"",
                  ""gamLine"": ""Played <value>""
                },
                ""Throttle"": {
                  ""kind"": ""number"",
                  ""storage"": ""gam"",
                  ""encoding"": ""integer"",
                  ""gamLine"": ""Throttle <value>""
                },
                ""Effects"": {
                  ""kind"": ""number"",
                  ""storage"": ""gam"",
                  ""encoding"": ""integer"",
                  ""gamLine"": ""Effects <value>""
                },
                ""Version"": {
                  ""kind"": ""number"",
                  ""storage"": ""gam"",
                  ""encoding"": ""integer"",
                  ""gamLine"": ""Version <value>""
                },
                ""DX"": {
                  ""kind"": ""number"",
                  ""storage"": ""gam"",
                  ""encoding"": ""integer"",
                  ""gamLine"": ""DX <value>""
                },
                ""Layout"": {
                  ""kind"": ""text"",
                  ""storage"": ""gam"",
                  ""encoding"": ""gam-line"",
                  ""gamLine"": ""Layout <text>""
                },
                ""Game"": {
                  ""kind"": ""text"",
                  ""storage"": ""gam"",
                  ""encoding"": ""gam-line"",
                  ""gamLine"": ""Game <text>""
                },
                ""ROM"": {
                  ""kind"": ""text"",
                  ""storage"": ""gam"",
                  ""encoding"": ""gam-line"",
                  ""gamLine"": ""ROM <text>""
                }
              }
            }";

        public static GameConfigMap Create() =>
            GameConfigMap.FromJson(SystemName, "embedded:" + SystemName, Json);
    }
}
