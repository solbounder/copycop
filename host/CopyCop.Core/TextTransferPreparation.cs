using System.Text;

namespace CopyCop.Core;

public sealed record PreparedTextTransfer(string Text, string Hint, string? Error = null);

/// <summary>Keeps the editable source separate from the optional keyboard-safe package.</summary>
public sealed class TextTransferPreparation
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private string? previousSource;
    private bool previousCompression;
    public PreparedTextTransfer Result { get; private set; } = new("", "");

    public PreparedTextTransfer Prepare(string source, bool compress)
    {
        if (source == previousSource && compress == previousCompression) return Result;
        previousSource = source;
        previousCompression = compress;
        try
        {
            if (!compress || source.Length == 0)
                return Result = new(source, compress
                    ? "Text eingeben. Am Ziel wird das Paket in copycop.html entpackt."
                    : "Direktes Tippen des Textes ohne Paket.");
            if (CopyCopBundle.LooksLikeBundle(source))
            {
                CopyCopBundle.Read(source);
                return Result = new(source, "Bereits ein Dateipaket. Es wird unverändert übertragen und in copycop.html entpackt.");
            }
            if (Utf8.GetByteCount(source) > CopyCopBundle.MaximumExpandedBytes)
                throw new InvalidDataException("Der Text ist größer als 16 MiB.");
            var bundle = CopyCopBundle.Create([new BundleFile("text.txt", Utf8.GetBytes(source))], compress: true);
            var originalCharacters = source.ReplaceLineEndings("\n").EnumerateRunes().Count();
            var difference = 100d * (originalCharacters - bundle.Text.Length) / originalCharacters;
            var comparison = difference >= 0 ? $"{difference:N1} % weniger" : $"{-difference:N1} % mehr";
            return Result = new(bundle.Text,
                $"{originalCharacters:N0} Textzeichen → {bundle.Text.Length:N0} Paketzeichen ({comparison}). "
                + "Am Ziel in copycop.html entpacken und text.txt speichern. Der Originaltext bleibt erhalten.");
        }
        catch (Exception exception) when (exception is InvalidDataException or EncoderFallbackException)
        {
            return Result = new("", $"Kompression fehlgeschlagen: {exception.Message}", exception.Message);
        }
    }
}
