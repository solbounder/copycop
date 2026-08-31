using System.Text;

namespace CopyCop.Core;

public sealed record UnsupportedCharacter(int CodePoint, string Display, int CharacterIndex);
public sealed record TextAnalysis(
    string Text,
    int OriginalCharacterCount,
    int NormalizedCharacterCount,
    IReadOnlyList<UnsupportedCharacter> Unsupported);

public static class UnicodeAnalyzer
{
    public static TextAnalysis Analyze(string input, bool replaceUnsupported)
    {
        var lineNormalized = input.Replace("\r\n", "\n", StringComparison.Ordinal)
                                  .Replace('\r', '\n');
        var output = new StringBuilder(lineNormalized.Length);
        var unsupported = new List<UnsupportedCharacter>();
        var originalCount = 0;

        foreach (var original in lineNormalized.EnumerateRunes())
        {
            var rune = Normalize(original);
            if (GermanLayout.TryMap(rune, out _))
            {
                output.Append(rune.ToString());
            }
            else
            {
                unsupported.Add(new UnsupportedCharacter(
                    rune.Value, rune.ToString(), originalCount));
                output.Append(replaceUnsupported ? "?" : rune.ToString());
            }
            originalCount++;
        }

        var normalizedText = output.ToString();
        return new TextAnalysis(
            normalizedText,
            originalCount,
            normalizedText.EnumerateRunes().Count(),
            unsupported);
    }

    private static Rune Normalize(Rune rune) => rune.Value switch
    {
        0x201C or 0x201D or 0x201E or 0x00AB or 0x00BB => new Rune('"'),
        0x2018 or 0x2019 or 0x201A => new Rune('\''),
        0x2013 or 0x2014 => new Rune('-'),
        0x00A0 => new Rune(' '),
        _ => rune,
    };
}
