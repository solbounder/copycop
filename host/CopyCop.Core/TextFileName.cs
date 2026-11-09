using System.Text;
using System.Text.RegularExpressions;

namespace CopyCop.Core;

/// <summary>Derives a readable file name for packaged text from its first words.</summary>
public static class TextFileName
{
    public const string Fallback = "text.txt";
    private const int MaximumWords = 3;
    private const int MaximumLength = 40;
    private static readonly Regex Word = new(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string FromText(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var name = new StringBuilder();
            var words = 0;
            foreach (Match match in Word.Matches(line))
            {
                var separator = name.Length > 0 ? 1 : 0;
                var room = MaximumLength - name.Length - separator;
                if (room <= 0) break;
                if (separator == 1) name.Append('-');
                name.Append(match.Value.Length > room ? TrimToRunes(match.Value, room) : match.Value);
                if (++words == MaximumWords) break;
            }
            if (name.Length == 0) continue;
            var candidate = name.ToString() + ".txt";
            try
            {
                CopyCopBundle.ValidateName(candidate);
                return candidate;
            }
            catch (InvalidDataException)
            {
                return Fallback;
            }
        }
        return Fallback;
    }

    private static string TrimToRunes(string value, int maximumChars)
    {
        var builder = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (builder.Length + rune.Utf16SequenceLength > maximumChars) break;
            builder.Append(rune.ToString());
        }
        return builder.ToString();
    }
}
