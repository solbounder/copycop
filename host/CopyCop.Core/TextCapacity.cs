using System.Text;

namespace CopyCop.Core;

public sealed record TextAssessment(
    TextAnalysis Analysis,
    int Utf8Bytes,
    int MaximumBytes,
    int RequiredParts,
    bool ReplacedUnsupported)
{
    public bool HasText => Analysis.NormalizedCharacterCount > 0;
    public bool HasUnsupported => Analysis.Unsupported.Count > 0;
    public bool FitsCapacity => Utf8Bytes <= MaximumBytes;
    public bool HasBlockingUnsupported => HasUnsupported && !ReplacedUnsupported;
    public bool CanTransfer => HasText && !HasBlockingUnsupported && FitsCapacity;
    public double UsagePercent => MaximumBytes == 0
        ? 0d : Math.Min(100d, Utf8Bytes * 100d / MaximumBytes);
}

public static class TextCapacity
{
    public const int FirmwareMaximumBytes = 0x1EE00;

    public static TextAssessment Assess(
        string text,
        bool replaceUnsupported,
        int maximumBytes = FirmwareMaximumBytes)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var analysis = UnicodeAnalyzer.Analyze(text ?? string.Empty, replaceUnsupported);
        var byteCount = Encoding.UTF8.GetByteCount(analysis.Text);
        var parts = byteCount == 0 ? 0 : (byteCount <= maximumBytes
            ? 1 : TextSplitter.Split(analysis.Text, maximumBytes).Count);
        return new TextAssessment(analysis, byteCount, maximumBytes, parts,
            replaceUnsupported);
    }
}
