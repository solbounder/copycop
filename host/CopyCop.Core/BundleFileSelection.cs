namespace CopyCop.Core;

/// <summary>Transactional, additive file selection shared by desktop and Android.</summary>
public sealed class BundleFileSelection
{
    private readonly List<BundleFile> files = [];
    public IReadOnlyList<BundleFile> Files => files.AsReadOnly();
    public long TotalBytes => files.Sum(file => (long)file.Data.Length);

    public int Add(IReadOnlyList<BundleFile> incoming)
    {
        var combined = new List<BundleFile>(files);
        var names = combined.ToDictionary(file => file.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var file in incoming)
        {
            if (names.TryGetValue(file.Name, out var existing))
            {
                if (!existing.Data.AsSpan().SequenceEqual(file.Data))
                    throw new InvalidDataException($"„{file.Name}“ ist bereits mit anderem Inhalt ausgewählt. Bitte den bisherigen Eintrag zuerst entfernen.");
                continue;
            }
            combined.Add(file);
            names.Add(file.Name, file);
        }
        if (combined.Count == 0) return 0;
        CopyCopBundle.ValidateFiles(combined);
        var added = combined.Count - files.Count;
        files.Clear();
        files.AddRange(combined);
        return added;
    }

    public void Replace(IReadOnlyList<BundleFile> replacement)
    {
        if (replacement.Count > 0) CopyCopBundle.ValidateFiles(replacement);
        var snapshot = replacement.ToArray();
        files.Clear();
        files.AddRange(snapshot);
    }

    public bool Remove(string name) => files.RemoveAll(file =>
        file.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;

    public void Clear() => files.Clear();
}
