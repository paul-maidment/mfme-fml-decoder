using System;
using MfmeFmlDecoder.Model;
using MfmeFmlDecoder.src.Model.Component;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>Maps a decoded component model type back to its on-disk MFME kind id.</summary>
    internal static class ComponentKind
    {
        public static MFMEComponentType Resolve(BaseComponent component)
        {
            if (component is null) throw new ArgumentNullException(nameof(component));

            string name = component.GetType().Name;
            if (Enum.TryParse(name, ignoreCase: false, out MFMEComponentType kind))
            {
                return kind;
            }

            throw new InvalidOperationException(
                $"Component model type '{name}' has no MFMEComponentType mapping.");
        }

        public static uint ResolveId(BaseComponent component) => (uint)Resolve(component);
    }
}
