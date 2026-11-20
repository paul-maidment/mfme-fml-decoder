using System;
using System.Collections.Generic;
using MfmeFmlDecoder.src.Model.Component;

namespace MfmeFmlDecoder.Model
{
    /// <summary>
    /// Creates component model instances from the <c>Type</c> discriminator written into
    /// <c>layout.json</c>. Names match the concrete class names (and the
    /// <see cref="MFMEComponentType"/> enum). Kept explicit rather than reflective so the
    /// path stays Native-AOT safe.
    /// </summary>
    internal static class ComponentFactory
    {
        private static readonly Dictionary<string, Func<BaseComponent>> Factories =
            new Dictionary<string, Func<BaseComponent>>(StringComparer.Ordinal)
            {
                ["AceMatrix"] = () => new AceMatrix(),
                ["AceVideo"] = () => new AceVideo(),
                ["Alpha"] = () => new Alpha(),
                ["AlphaNew"] = () => new AlphaNew(),
                ["AstraMatrix"] = () => new AstraMatrix(),
                ["Background"] = () => new Background(),
                ["BandReel"] = () => new BandReel(),
                ["BarcrestBWBVideo"] = () => new BarcrestBWBVideo(),
                ["BFMAlpha"] = () => new BFMAlpha(),
                ["BFMColourLed"] = () => new BFMColourLed(),
                ["BFMLED"] = () => new BFMLED(),
                ["BFMVideo"] = () => new BFMVideo(),
                ["Bitmap"] = () => new Bitmap(),
                ["Border"] = () => new Border(),
                ["Button"] = () => new Button(),
                ["Checkbox"] = () => new Checkbox(),
                ["CoinmasterVideo"] = () => new CoinmasterVideo(),
                ["DiscReel"] = () => new DiscReel(),
                ["DotAlpha"] = () => new DotAlpha(),
                ["DotMatrix"] = () => new DotMatrix(),
                ["EpochDotAlpha"] = () => new EpochDotAlpha(),
                ["EpochMatrix"] = () => new EpochMatrix(),
                ["FlipReel"] = () => new FlipReel(),
                ["Frame"] = () => new Frame(),
                ["IGTVfd"] = () => new IGTVfd(),
                ["Label"] = () => new Label(),
                ["Lamp"] = () => new Lamp(),
                ["Led"] = () => new Led(),
                ["MatrixAlpha"] = () => new MatrixAlpha(),
                ["MaygayMatrix"] = () => new MaygayMatrix(),
                ["MaygayVideo"] = () => new MaygayVideo(),
                ["PlasmaDisplay"] = () => new PlasmaDisplay(),
                ["PrismLamp"] = () => new PrismLamp(),
                ["ProconnMatrix"] = () => new ProconnMatrix(),
                ["Reel"] = () => new Reel(),
                ["ReelBonusReel"] = () => new ReelBonusReel(),
                ["RGBLed"] = () => new RGBLed(),
                ["SevenSeg"] = () => new SevenSeg(),
                ["SevenSegBlock"] = () => new SevenSegBlock(),
            };

        public static bool TryCreate(string typeName, out BaseComponent component)
        {
            component = null;
            if (string.IsNullOrEmpty(typeName))
            {
                return false;
            }

            if (Factories.TryGetValue(typeName, out Func<BaseComponent> factory))
            {
                component = factory();
                return true;
            }

            return false;
        }

        public static BaseComponent Create(string typeName)
        {
            if (!TryCreate(typeName, out BaseComponent component))
            {
                throw new InvalidOperationException($"Unknown component type '{typeName}' in layout.json.");
            }

            return component;
        }
    }
}
