using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CopyCop.Core;

public sealed record BundleFile(string Name, byte[] Data)
{
    public override string ToString() => $"{Name} · {Data.Length:N0} Bytes";
}
public sealed record BundleResult(string Text, int FileCount, long OriginalBytes,
    int ArchiveBytes, int UncompressedArchiveBytes);

/// <summary>A ZIP archive carried in numbered, checksummed, keyboard-safe text frames.</summary>
public static class CopyCopBundle
{
    public const int MaximumFiles = 256;
    public const int MaximumExpandedBytes = 16 * 1024 * 1024;
    public const int MaximumParts = 4096;
    private const int LineWidth = 120;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex FramePattern = new(
        @"\GCOPYCOP/1 ([0-9a-fA-F]{64}) ([1-9][0-9]{0,3})/([1-9][0-9]{0,3})\r?\n([A-Za-z0-9+/=\r\n\t ]+?)\r?\nENDCOPYCOP(?:\r?\n|$)",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    public static bool LooksLikeBundle(string text) =>
        text.TrimStart().StartsWith("COPYCOP/", StringComparison.Ordinal);

    public static BundleResult Create(IReadOnlyList<BundleFile> files, bool compress = true)
    {
        ValidateFiles(files);
        var plain = CreateArchive(files, CompressionLevel.NoCompression);
        var archive = compress ? CreateArchive(files, CompressionLevel.SmallestSize) : plain;
        if (archive.Length >= plain.Length) archive = plain;
        return new BundleResult(Encode(archive), files.Count, files.Sum(file => (long)file.Data.Length),
            archive.Length, plain.Length);
    }

    public static IReadOnlyList<BundleFile> Read(string text) => ReadArchive(Decode(text));

    public static string PrepareForSave(string text, bool compress = true)
    {
        if (LooksLikeBundle(text))
        {
            Read(text);
            return text;
        }
        return Create([new BundleFile("text.txt", Utf8.GetBytes(text))], compress).Text;
    }

    public static async Task<BundleFile> ReadFileAsync(string name, Stream stream,
        int maximumBytes = MaximumExpandedBytes, CancellationToken cancellationToken = default)
    {
        ValidateName(name);
        if (maximumBytes < 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0,
                Math.Min(buffer.Length, maximumBytes - (int)output.Length + 1)), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (output.Length + count > maximumBytes)
                throw new InvalidDataException("Die Dateien sind zusammen größer als 16 MiB.");
            output.Write(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new BundleFile(name, output.ToArray());
    }

    public static byte[] Decode(string text)
    {
        if (text.Length > TextFileReader.MaximumFileBytes)
            throw new InvalidDataException("Das CopyCop-Paket ist größer als 4 MiB.");
        text = text.Trim();
        var chunks = new SortedDictionary<int, string>();
        string? hash = null;
        var total = 0;
        var position = 0;
        while (position < text.Length)
        {
            var match = FramePattern.Match(text, position);
            if (!match.Success)
                throw new InvalidDataException("Ungültiges oder unvollständiges CopyCop-Paket (Version 1 erwartet).");
            var partHash = match.Groups[1].Value.ToLowerInvariant();
            var number = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var count = int.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (count > MaximumParts || number > count)
                throw new InvalidDataException("Ungültige CopyCop-Teilnummer.");
            if (hash is not null && (hash != partHash || total != count))
                throw new InvalidDataException("Die Teile gehören nicht zum selben CopyCop-Paket.");
            hash = partHash;
            total = count;
            var data = string.Concat(match.Groups[4].Value.Where(c => c is not (' ' or '\t' or '\r' or '\n')));
            if (chunks.TryGetValue(number, out var existing) && existing != data)
                throw new InvalidDataException($"Teil {number} wurde mit unterschiedlichem Inhalt übertragen.");
            chunks[number] = data;
            position = match.Index + match.Length;
            while (position < text.Length && text[position] is ' ' or '\t' or '\r' or '\n') position++;
        }
        if (total == 0 || chunks.Count != total)
            throw new InvalidDataException($"CopyCop-Paket unvollständig: {chunks.Count} von {total} Teilen vorhanden.");
        byte[] archive;
        try
        {
            var encoded = string.Concat(chunks.Values);
            archive = Convert.FromBase64String(encoded);
            if (Convert.ToBase64String(archive) != encoded) throw new FormatException();
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("Die CopyCop-Nutzdaten sind beschädigt (Base64).", exception);
        }
        if (!Convert.ToHexString(SHA256.HashData(archive)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Prüfsumme stimmt nicht. Bitte die CopyCop-Teile erneut übertragen.");
        return archive;
    }

    public static string Encode(byte[] archive, int maximumPartBytes = TextCapacity.FirmwareMaximumBytes) =>
        string.Concat(FrameArchive(archive, maximumPartBytes));

    public static IReadOnlyList<TextPart> SplitForTransfer(string text, int maximumPartBytes)
    {
        return FrameArchive(Decode(text), maximumPartBytes)
            .Select((part, index) => new TextPart(index + 1, part, part.Length, part.Length)).ToArray();
    }

    private static IReadOnlyList<string> FrameArchive(byte[] archive, int maximumPartBytes)
    {
        if (archive.Length > TextFileReader.MaximumFileBytes * 3 / 4)
            throw new InvalidDataException("Das erzeugte CopyCop-Paket ist größer als 4 MiB. Bitte weniger Dateien auswählen.");
        if (maximumPartBytes < 256)
            throw new ArgumentOutOfRangeException(nameof(maximumPartBytes), "CopyCop-Teile benötigen mindestens 256 Bytes.");
        // Reserve header/footer space, including four-digit part numbers and line breaks.
        var chunkSize = (int)Math.Min((long)(maximumPartBytes - 128) * LineWidth / (LineWidth + 1),
            TextFileReader.MaximumFileBytes) / 4 * 4;
        var data = Convert.ToBase64String(archive);
        var count = Math.Max(1, (data.Length + chunkSize - 1) / chunkSize);
        if (count > MaximumParts) throw new InvalidDataException("Zu viele CopyCop-Teile.");
        var hash = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        var parts = new List<string>(count);
        var totalSize = 0;
        for (var index = 0; index < count; index++)
        {
            var builder = new StringBuilder($"COPYCOP/1 {hash} {index + 1}/{count}\n");
            var end = Math.Min(data.Length, (index + 1) * chunkSize);
            for (var offset = index * chunkSize; offset < end; offset += LineWidth)
                builder.Append(data, offset, Math.Min(LineWidth, end - offset)).Append('\n');
            builder.Append("ENDCOPYCOP\n");
            var part = builder.ToString();
            totalSize += part.Length;
            if (totalSize > TextFileReader.MaximumFileBytes)
                throw new InvalidDataException("Das erzeugte CopyCop-Paket ist größer als 4 MiB. Bitte weniger Dateien auswählen.");
            parts.Add(part);
        }
        return parts;
    }

    public static IReadOnlyList<BundleFile> ReadArchive(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, Utf8);
        if (archive.Entries.Count is 0 or > MaximumFiles)
            throw new InvalidDataException($"Ein Paket muss 1 bis {MaximumFiles} Dateien enthalten.");
        var files = new List<BundleFile>();
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateName(entry.FullName);
            var mode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (mode is not (0 or 0x8000))
                throw new InvalidDataException("Das Paket darf nur reguläre Dateien enthalten.");
            total += entry.Length;
            if (entry.Length < 0 || total > MaximumExpandedBytes)
                throw new InvalidDataException("Das entpackte Paket ist größer als 16 MiB.");
            using var input = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > entry.Length)
                    throw new InvalidDataException("Ungültige Dateigröße im ZIP-Archiv.");
                output.Write(buffer, 0, read);
            }
            if (output.Length != entry.Length) throw new InvalidDataException("Unvollständige ZIP-Datei.");
            var data = output.ToArray();
            if (Crc32.Compute(data) != entry.Crc32)
                throw new InvalidDataException($"ZIP-Prüfsumme stimmt nicht: {entry.FullName}");
            files.Add(new BundleFile(entry.FullName, data));
        }
        ValidateFiles(files);
        return files;
    }

    public static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || Utf8.GetByteCount(name) > 1024
            || name.Any(c => char.IsControl(c) || "\\:*?\"<>|".Contains(c)))
            throw new InvalidDataException("Ungültiger Dateiname im CopyCop-Paket.");
        foreach (var component in name.Split('/'))
        {
            var stem = component.Split('.')[0];
            if (component is "" or "." or ".." || component.Length > 240
                || component.EndsWith(' ') || component.EndsWith('.')
                || Regex.IsMatch(stem, @"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException($"Nicht portabler Dateipfad: {name}");
        }
    }

    internal static void ValidateFiles(IReadOnlyList<BundleFile> files)
    {
        if (files.Count is 0 or > MaximumFiles)
            throw new InvalidDataException($"Bitte 1 bis {MaximumFiles} Dateien auswählen.");
        long size = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            ValidateName(file.Name);
            if (!names.Add(file.Name)) throw new InvalidDataException($"Doppelter Dateiname: {file.Name}");
            size += file.Data.Length;
            if (size > MaximumExpandedBytes) throw new InvalidDataException("Die Dateien sind zusammen größer als 16 MiB.");
        }
        foreach (var name in names)
        {
            var index = name.IndexOf('/');
            while (index >= 0)
            {
                if (names.Contains(name[..index]))
                    throw new InvalidDataException($"Datei und Ordner haben denselben Namen: {name[..index]}");
                index = name.IndexOf('/', index + 1);
            }
        }
    }

    private static byte[] CreateArchive(IReadOnlyList<BundleFile> files, CompressionLevel compression)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Utf8))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Name, compression);
                entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var content = entry.Open();
                content.Write(file.Data);
            }
        }
        return output.ToArray();
    }
}
