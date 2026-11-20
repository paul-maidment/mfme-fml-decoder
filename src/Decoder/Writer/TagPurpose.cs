using System.Text;

namespace MfmeFmlDecoder.Decoder.Writer
{
    /// <summary>
    /// Inverse of the decoder's image-purpose slug. Image dictionary keys are the slugified
    /// <see cref="Model.TagInfo.AttributeName"/>, so the encoder must reproduce the same slug
    /// to map a model image back to its tag.
    /// </summary>
    internal static class TagPurpose
    {
        public static string Slugify(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return null;
            }

            StringBuilder sb = new StringBuilder(source.Length);
            bool prevWasSeparator = false;
            bool prevWasLowerOrDigit = false;

            foreach (char c in source)
            {
                if (char.IsLetterOrDigit(c))
                {
                    if (char.IsUpper(c) && prevWasLowerOrDigit && !prevWasSeparator && sb.Length > 0)
                    {
                        sb.Append('_');
                    }

                    sb.Append(char.ToLowerInvariant(c));
                    prevWasSeparator = false;
                    prevWasLowerOrDigit = char.IsLower(c) || char.IsDigit(c);
                    continue;
                }

                if (!prevWasSeparator && sb.Length > 0)
                {
                    sb.Append('_');
                    prevWasSeparator = true;
                }

                prevWasLowerOrDigit = false;
            }

            while (sb.Length > 0 && sb[sb.Length - 1] == '_')
            {
                sb.Length--;
            }

            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}
