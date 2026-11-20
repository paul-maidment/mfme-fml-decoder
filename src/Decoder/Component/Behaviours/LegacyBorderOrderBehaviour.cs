using MfmeFmlDecoder.Decoder;
using System.Collections.Generic;
using System.Linq;
using MfmeFmlDecoder.src.Model.Component;

namespace MfmeFmlDecoder.src.Decoder.Component.Behaviours
{
    /// <summary>
    /// v19.8 writes container borders after their contents. 20.1 writes the background
    /// first, then borders in reverse file order, then the remaining elements.
    /// This is a structural ordering difference, not a value difference.
    /// </summary>
    internal static class LegacyBorderOrderBehaviour
    {
        public static void Apply(IList<BaseComponent> components, ComponentOrderingMode mode)
        {
            if (components is null || components.Count == 0)
            {
                return;
            }

            List<BaseComponent> backgrounds = components.Where(c => c is Background).ToList();
            List<BaseComponent> borders = components.OfType<Border>().Cast<BaseComponent>().ToList();
            if (borders.Count == 0)
            {
                return;
            }

            List<BaseComponent> rest = components
                .Where(c => c is not Background && c is not Border)
                .ToList();

            // MFME's canonical 20.1 order is: backgrounds, then borders in reverse file order,
            // then the rest. Old versions always write borders after their contents, and a number
            // of 20.1 files do too (borders trailing). Canonicalise whenever the borders trail the
            // rest; leave already-canonical files untouched so 20.1 -> 20.1 stays byte-identical.
            bool bordersTrailing = rest.Count > 0
                && borders.Min(c => components.IndexOf(c)) > rest.Max(c => components.IndexOf(c));

            if (mode != ComponentOrderingMode.BordersLastReversed && !bordersTrailing)
            {
                return;
            }

            components.Clear();
            foreach (BaseComponent component in backgrounds)
            {
                components.Add(component);
            }

            for (int i = borders.Count - 1; i >= 0; i--)
            {
                components.Add(borders[i]);
            }

            foreach (BaseComponent component in rest)
            {
                components.Add(component);
            }
        }
    }
}
