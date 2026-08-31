using System.Text;

namespace CopyCop.Core;

public sealed record TextPart(int Number, string Text, int CharacterCount, int Utf8Bytes)
{
    public override string ToString() =>
        $"Teil {Number} · {CharacterCount:N0} Zeichen · {Utf8Bytes:N0} Bytes";
}

public static class TextSplitter
{
    private readonly record struct Unit(
        int CharacterStart,
        int ByteEnd,
        bool IsWhitespaceBoundary,
        bool IsLineBoundary);

    public static IReadOnlyList<TextPart> Split(string text, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (text.Length == 0) return [];

        var units = new List<Unit>();
        var characterStart = 0;
        var byteEnd = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            byteEnd += rune.Utf8SequenceLength;
            var isLine = rune.Value == '\n';
            units.Add(new Unit(
                characterStart,
                byteEnd,
                isLine || Rune.IsWhiteSpace(rune),
                isLine));
            characterStart += rune.Utf16SequenceLength;
        }

        var parts = new List<TextPart>();
        var start = 0;
        while (start < units.Count)
        {
            var byteStart = start == 0 ? 0 : units[start - 1].ByteEnd;
            var end = start;
            while (end < units.Count && units[end].ByteEnd - byteStart <= maximumBytes)
                end++;

            if (end == start)
                throw new InvalidOperationException("Ein einzelnes Unicode-Zeichen überschreitet die Teilgröße.");

            var cut = end;
            if (end < units.Count)
            {
                var minimumCleanBytes = maximumBytes * 60 / 100;
                var lineCut = FindBoundary(units, start, end, byteStart,
                    minimumCleanBytes, lineOnly: true);
                var whitespaceCut = FindBoundary(units, start, end, byteStart,
                    minimumCleanBytes, lineOnly: false);
                cut = lineCut > start ? lineCut
                    : whitespaceCut > start ? whitespaceCut
                    : end;
            }

            var utf16Start = units[start].CharacterStart;
            var utf16End = cut == units.Count ? text.Length : units[cut].CharacterStart;
            var partText = text[utf16Start..utf16End];
            var partBytes = units[cut - 1].ByteEnd - byteStart;
            parts.Add(new TextPart(parts.Count + 1, partText, cut - start, partBytes));
            start = cut;
        }

        return parts;
    }

    private static int FindBoundary(
        IReadOnlyList<Unit> units,
        int start,
        int end,
        int byteStart,
        int minimumBytes,
        bool lineOnly)
    {
        for (var index = end - 1; index >= start; index--)
        {
            var unit = units[index];
            if (unit.ByteEnd - byteStart < minimumBytes) break;
            if (lineOnly ? unit.IsLineBoundary : unit.IsWhitespaceBoundary)
                return index + 1;
        }
        return start;
    }
}
