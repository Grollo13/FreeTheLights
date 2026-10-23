using System.Globalization;
using System.Text;

namespace FreeTheLights.CarData
{
    /// <summary>
    /// Normalizes SimHub game names and car ids the way lovely-car-data names its folders and files:
    /// lowercase, accents removed, special characters replaced by hyphens, double hyphens collapsed,
    /// leading/trailing hyphens trimmed. Example: "AIX Racing 24" → "aix-racing-24".
    /// </summary>
    public static class NameNormalizer
    {
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            var lastWasHyphen = false;

            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    builder.Append(c);
                    lastWasHyphen = false;
                }
                else if (!lastWasHyphen)
                {
                    builder.Append('-');
                    lastWasHyphen = true;
                }
            }

            return builder.ToString().Trim('-');
        }
    }
}
