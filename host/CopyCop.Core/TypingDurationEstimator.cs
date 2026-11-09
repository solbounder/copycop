using System.Text;

namespace CopyCop.Core;

public readonly record struct TypingWorkload(long StrokeCount, long AltGrStrokeCount, long ShiftStrokeCount = 0)
{
    public TimeSpan Estimate(int interKeyDelayMilliseconds)
    {
        if (interKeyDelayMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(interKeyDelayMilliseconds));

        var regularStrokes = StrokeCount - AltGrStrokeCount - ShiftStrokeCount;
        var milliseconds = checked(
            regularStrokes * TypingDurationEstimator.KeyHoldMilliseconds
            + ShiftStrokeCount * TypingDurationEstimator.ShiftStrokeMilliseconds
            + AltGrStrokeCount * TypingDurationEstimator.AltGrStrokeMilliseconds
            + StrokeCount * interKeyDelayMilliseconds);
        return TimeSpan.FromTicks(checked(milliseconds * TimeSpan.TicksPerMillisecond));
    }
}

public static class TypingDurationEstimator
{
    // Mirrors firmware/include/board_config.h.
    public const int KeyHoldMilliseconds = 10;
    public const int ShiftSettleMilliseconds = 35;
    public const int ShiftKeyHoldMilliseconds = 15;
    public const int ShiftStrokeMilliseconds = ShiftSettleMilliseconds * 2 + ShiftKeyHoldMilliseconds;
    public const int AltGrSettleMilliseconds = 35;
    public const int AltGrKeyHoldMilliseconds = 15;
    public const int AltGrStrokeMilliseconds =
        AltGrSettleMilliseconds * 2 + AltGrKeyHoldMilliseconds;

    public static IReadOnlyList<int> SpeedLevelsMilliseconds { get; } =
        [10, 25, 50, 100, 250, 500, 750, 1000];

    public static TypingWorkload Analyze(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        long strokes = 0;
        long altGrStrokes = 0;
        long shiftStrokes = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (!GermanLayout.TryMap(rune, out var sequence))
                throw new ArgumentException($"Nicht unterstütztes Zeichen U+{rune.Value:X4}.", nameof(text));

            foreach (var stroke in sequence.Strokes)
            {
                strokes++;
                if ((stroke.Modifiers & GermanLayout.AltGr) != 0) altGrStrokes++;
                else if (stroke.Modifiers != 0) shiftStrokes++;
            }
        }

        return new TypingWorkload(strokes, altGrStrokes, shiftStrokes);
    }

    public static string Format(TimeSpan duration)
    {
        if (duration.TotalSeconds < 1)
            return $"{Math.Round(duration.TotalMilliseconds):N0} ms";
        if (duration.TotalMinutes < 1)
            return duration.TotalSeconds < 10
                ? $"{duration.TotalSeconds:0.0} s"
                : $"{duration.TotalSeconds:0} s";
        if (duration.TotalHours < 1)
            return $"{(int)duration.TotalMinutes} min {duration.Seconds:00} s";
        if (duration.TotalDays < 1)
            return $"{(int)duration.TotalHours} h {duration.Minutes:00} min";
        return $"{(int)duration.TotalDays} d {duration.Hours:00} h";
    }
}
