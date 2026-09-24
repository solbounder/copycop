using System.IO.Compression;
using System.Text;
using CopyCop.Core;
using CopyCop.Gui.ViewModels;

internal static class BundleTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var files = new BundleFile[]
        {
            new("src/Grüße.cs", Encoding.UTF8.GetBytes("\uFEFF// ä 😀\r\n\treturn \"你好\";\r\n")),
            new("bytes.dat", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray()),
            new("leer.txt", []),
            new("wiederholt.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Hallo Welt!\r\n", 5000)))),
        };
        foreach (var compress in new[] { false, true })
        {
            var result = CopyCopBundle.Create(files, compress);
            var restored = CopyCopBundle.Read(result.Text);
            check(restored.Count == files.Length && restored.Zip(files).All(pair =>
                pair.First.Name == pair.Second.Name && pair.First.Data.SequenceEqual(pair.Second.Data)),
                $"bundle byte-exact names, BOM, CRLF, tabs, Unicode, binary and empty files ({compress})");
            var normalized = UnicodeAnalyzer.Analyze(result.Text, false);
            check(normalized.Text == result.Text && normalized.Unsupported.Count == 0,
                "bundle is unchanged by keyboard normalization");
            check(!compress || result.ArchiveBytes < result.UncompressedArchiveBytes / 4,
                "compressible content saves most typing bytes");
            check(result.Text == CopyCopBundle.Create(files, compress).Text, "deterministic bundle encoding");
        }

        var random = new byte[300_000];
        new Random(42).NextBytes(random);
        var large = CopyCopBundle.Create([new BundleFile("random.dat", random)]);
        check(large.ArchiveBytes <= large.UncompressedArchiveBytes, "compression never grows archive");
        var parts = TextSplitter.Split(large.Text, TextCapacity.FirmwareMaximumBytes);
        check(parts.Count > 1 && parts.All(part => part.Text.StartsWith("COPYCOP/1 ")
            && part.Utf8Bytes <= TextCapacity.FirmwareMaximumBytes
            && part.Text.EndsWith("ENDCOPYCOP\n")), "independent bounded frames at device capacity");
        check(TextCapacity.Assess(large.Text, false).RequiredParts == parts.Count, "capacity counts framed parts");
        var reordered = string.Concat(parts.Reverse().Select(part => part.Text));
        check(CopyCopBundle.Read(reordered)[0].Data.SequenceEqual(random), "out-of-order parts reassemble");
        check(CopyCopBundle.Read(reordered + parts[0].Text)[0].Data.SequenceEqual(random),
            "identical duplicate is harmless");
        check(CopyCopBundle.Read(reordered.Replace("\n", "\r\n"))[0].Data.SequenceEqual(random),
            "CRLF from target editor accepted");
        var smaller = CopyCopBundle.SplitForTransfer(large.Text, 4096);
        check(smaller.All(part => part.Utf8Bytes <= 4096)
            && CopyCopBundle.Read(string.Concat(smaller.Select(part => part.Text)))[0].Data.SequenceEqual(random),
            "reframing for a smaller device retains content");
        Reject(() => CopyCopBundle.Read(parts[0].Text), check, "missing part rejected");
        Reject(() => CopyCopBundle.Read(reordered + "junk"), check, "trailing garbage rejected");
        Reject(() => CopyCopBundle.Read(reordered.Replace("COPYCOP/1", "COPYCOP/2")), check, "future version rejected");
        var other = CopyCopBundle.Create([new BundleFile("other.txt", [1, 2])]);
        Reject(() => CopyCopBundle.Read(reordered + other.Text), check, "mixed bundles rejected");
        var corrupt = parts[0].Text.ToCharArray();
        var dataStart = parts[0].Text.IndexOf('\n') + 1;
        corrupt[dataStart] = corrupt[dataStart] == 'A' ? 'B' : 'A';
        Reject(() => CopyCopBundle.Read(reordered + new string(corrupt)), check, "conflicting duplicate rejected");
        Reject(() => CopyCopBundle.Read(new string(corrupt) + string.Concat(parts.Skip(1).Select(p => p.Text))),
            check, "checksum catches transmission corruption");
        Reject(() => CopyCopBundle.Read(new string('x', TextFileReader.MaximumFileBytes + 1)), check,
            "oversize transmission rejected");

        foreach (var name in new[] { "../bad", "/absolute", "C:/drive", "dir\\bad", "AUX.txt", "a/../b", "a//b", "a.", "x\0z" })
            Reject(() => CopyCopBundle.Create([new BundleFile(name, [])]), check, "unsafe name: " + name);
        Reject(() => CopyCopBundle.Create([new BundleFile("A.txt", []), new BundleFile("a.txt", [])]),
            check, "duplicate portable filename rejected");
        Reject(() => CopyCopBundle.Create([new BundleFile("folder", []), new BundleFile("folder/x", [])]),
            check, "file-directory conflict rejected");
        Reject(() => CopyCopBundle.Create([]), check, "empty bundle rejected");
        Reject(() => CopyCopBundle.Create(Enumerable.Range(0, 257).Select(i => new BundleFile($"f{i}", [])).ToArray()),
            check, "too many files rejected");
        Reject(() => CopyCopBundle.Create([new BundleFile("large", new byte[CopyCopBundle.MaximumExpandedBytes + 1])]),
            check, "source bytes limit enforced");
        Reject(() => CopyCopBundle.Read(CopyCopBundle.Encode(Zip("../escape", [1]))), check,
            "foreign ZIP traversal rejected before extraction");
        Reject(() => CopyCopBundle.Read(CopyCopBundle.Encode(Zip("large", new byte[CopyCopBundle.MaximumExpandedBytes + 1]))),
            check, "decompression expansion limit enforced");
        using (var source = new MemoryStream([1, 2, 3]))
        {
            try { await CopyCopBundle.ReadFileAsync("input.dat", source, 2); check(false, "bounded source read"); }
            catch (InvalidDataException) { check(source.CanRead, "bounded source read leaves stream with caller"); }
        }
        await ViewModelAsync(check);
        var selection = new BundleFileSelection();
        check(selection.Add([new("a.txt", [1])]) == 1 && selection.Add([new("b.txt", [2])]) == 1,
            "shared selection appends individual picker results");
        check(selection.Add([new("a.txt", [1])]) == 0 && selection.Files.Count == 2,
            "shared selection deduplicates identical content");
        Reject(() => selection.Add([new("c.txt", [3]), new("A.txt", [9])]), check,
            "shared selection rejects conflicting batch");
        check(selection.Files.Count == 2 && selection.Files[0].Data[0] == 1,
            "conflict preserves entire shared selection");
        Reject(() => selection.Add([new("too-large", new byte[CopyCopBundle.MaximumExpandedBytes])]),
            check, "combined selections obey size limit");
        check(selection.Files.Count == 2, "size error is transactional");
        selection.Replace(selection.Files);
        check(selection.Files.Count == 2, "selection replacement supports own read-only snapshot");
        check(selection.Remove("A.txt") && selection.Files.Single().Name == "b.txt", "remove one selected file");
        selection.Clear();
        check(selection.Files.Count == 0, "explicit clear of shared selection");
    }

    private static async Task ViewModelAsync(Action<bool, string> check)
    {
        var pending = new TaskCompletionSource<IReadOnlyList<BundleFile>?>();
        var open = new TaskCompletionSource<(string Name, string Text)?>();
        string? saved = null;
        await using var model = new MainWindowViewModel(() => Task.FromResult<string?>(null),
            _ => open.Task, _ => pending.Task,
            (text, _) => { saved = text; return Task.FromResult<string?>("test.copycop"); },
            _ => Task.FromResult<IReadOnlyList<BundleFile>?>([new("ordner/extra.txt", [67])]));
        const string source = "Original\r\n\t😀";
        model.Text = source;
        var task = ExecuteAsync(model.BundleFilesCommand);
        check(!model.IsIdle && !model.OpenFileCommand.CanExecute(null)
            && !model.SaveBundleCommand.CanExecute(null), "file picker locks overlapping actions");
        pending.SetResult(null);
        await task;
        check(model.Text == source && model.IsIdle && !model.HasBundleFiles, "cancel picker preserves editor");
        await ExecuteAsync(model.SaveBundleCommand);
        check(CopyCopBundle.Read(saved!)[0].Data.SequenceEqual(Encoding.UTF8.GetBytes(source))
            && model.Text == source, "plain editor exports byte-exact text.txt");
        pending = new TaskCompletionSource<IReadOnlyList<BundleFile>?>();
        task = ExecuteAsync(model.BundleFilesCommand);
        pending.SetException(new IOException("lesefehler"));
        await task;
        check(model.Text == source && model.IsIdle && model.ActivityText.Contains("lesefehler"),
            "failed selection preserves editor");

        async Task Add(params BundleFile[] files)
        {
            pending = new TaskCompletionSource<IReadOnlyList<BundleFile>?>();
            var add = ExecuteAsync(model.BundleFilesCommand);
            pending.SetResult(files);
            await add;
        }
        await Add(new BundleFile("first.txt", [65]));
        await Add(new BundleFile("second.txt", [66]));
        check(model.BundleFiles.Select(f => f.Name).SequenceEqual(["first.txt", "second.txt"]),
            "separate one-file selections append on desktop");
        check(!model.SaveBundleCommand.CanExecute(null) && !model.CanSend,
            "pending file selection cannot save or send obsolete editor contents");
        await ExecuteAsync(model.BundleFolderCommand);
        check(model.BundleFiles.Count == 3, "folder selection appends to existing files");
        await Add(new BundleFile("first.txt", [65]));
        check(model.BundleFiles.Count == 3, "identical re-selection does not duplicate a file");
        await Add(new BundleFile("new.txt", [88]), new BundleFile("FIRST.txt", [90]));
        check(model.BundleFiles.Count == 3 && model.ActivityText.Contains("anderem Inhalt"),
            "conflicting batch keeps every existing file and adds nothing");
        pending = new TaskCompletionSource<IReadOnlyList<BundleFile>?>();
        task = ExecuteAsync(model.BundleFilesCommand);
        pending.SetResult(null);
        await task;
        check(model.BundleFiles.Count == 3, "cancelling later picker preserves entire list");
        model.CompressBundle = false;
        await ExecuteAsync(model.BuildBundleCommand);
        var bundleText = model.Text;
        check(CopyCopBundle.Read(bundleText).Count == 3 && model.SaveBundleCommand.CanExecute(null),
            "build includes all cumulative selections");
        await ExecuteAsync(model.SaveBundleCommand);
        check(saved == bundleText, "save does not double-wrap bundle");
        model.SelectedBundleFile = model.BundleFiles[1];
        model.RemoveBundleFileCommand.Execute(null);
        check(model.BundleFiles.Count == 2 && model.Text == "" && !model.SaveBundleCommand.CanExecute(null),
            "removing a source invalidates the obsolete package");
        await ExecuteAsync(model.BuildBundleCommand);
        check(CopyCopBundle.Read(model.Text).All(f => f.Name != "second.txt"), "removed file absent after rebuild");
        model.ClearBundleFilesCommand.Execute(null);
        check(!model.HasBundleFiles && model.Text == "" && !model.BuildBundleCommand.CanExecute(null),
            "explicit clear empties selection and generated package");
        var imported = CopyCopBundle.Create([new("imported.txt", [42])]);
        task = ExecuteAsync(model.OpenFileCommand);
        open.SetResult(("existing.copycop", imported.Text));
        await task;
        check(model.BundleFiles.Count == 1 && model.BundleFiles[0].Name == "imported.txt",
            "opening a bundle restores its editable file list");
        await Add(new BundleFile("added.txt", [43]));
        await ExecuteAsync(model.BuildBundleCommand);
        check(CopyCopBundle.Read(model.Text).Select(f => f.Name).SequenceEqual(["imported.txt", "added.txt"]),
            "additional file extends reopened package");
        await using var broken = new MainWindowViewModel(() => Task.FromResult<string?>(null),
            _ => Task.FromResult<(string, string)?>(("bad.copycop", "COPYCOP/1 invalid")));
        broken.Text = source;
        await ExecuteAsync(broken.OpenFileCommand);
        check(broken.Text == source && broken.ActivityText.Contains("nicht eingelesen"),
            "invalid bundle import preserves editor");
    }

    private static async Task ExecuteAsync(AsyncRelayCommand command)
    {
        var finished = new TaskCompletionSource();
        void Changed(object? sender, EventArgs args) { if (command.CanExecute(null)) finished.TrySetResult(); }
        command.CanExecuteChanged += Changed;
        try { command.Execute(null); await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { command.CanExecuteChanged -= Changed; }
    }

    private static byte[] Zip(string name, byte[] data)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        { using var entry = zip.CreateEntry(name).Open(); entry.Write(data); }
        return stream.ToArray();
    }

    private static void Reject(Action action, Action<bool, string> check, string name)
    {
        try { action(); check(false, name); }
        catch (InvalidDataException) { check(true, name); }
    }
}
