using System.Text;

namespace CopyCop.Core;

/// <summary>Reads Unicode text without silently replacing invalid bytes.</summary>
public static class TextFileReader
{
    public const int MaximumFileBytes = 4 * 1024 * 1024;

    public static async Task<string> ReadAsync(
        Stream stream, CancellationToken cancellationToken = default)
    {
        // Also enforce the limit on non-seekable streams and files that grow while reading.
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(chunk.AsMemory(
                0, Math.Min(chunk.Length, MaximumFileBytes + 1 - (int)buffer.Length)),
                cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            buffer.Write(chunk, 0, count);
            if (buffer.Length > MaximumFileBytes)
                throw new InvalidDataException("Die Datei ist zu groß. Maximal 4 MiB können eingelesen werden.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Decode(buffer.ToArray());
    }

    private static string Decode(byte[] bytes)
    {
        Encoding encoding = new UTF8Encoding(false, true);
        var offset = 0;
        // UTF-32 LE must be checked before UTF-16 LE because their BOMs overlap.
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            encoding = new UTF32Encoding(false, false, true);
            offset = 4;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            encoding = new UTF32Encoding(true, false, true);
            offset = 4;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            encoding = new UnicodeEncoding(false, false, true);
            offset = 2;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            encoding = new UnicodeEncoding(true, false, true);
            offset = 2;
        }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            offset = 3;
        }

        string text;
        try { text = encoding.GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "Die Datei enthält keine gültige Unicode-Textkodierung. Bitte als UTF-8 speichern.", exception);
        }

        if (text.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')))
            throw new InvalidDataException("Die Datei enthält binäre Daten oder nicht unterstützte Steuerzeichen. Bitte eine Textdatei auswählen.");
        return text;
    }
}
