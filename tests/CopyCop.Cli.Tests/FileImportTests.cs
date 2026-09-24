using System.Text;
using CopyCop.Core;
using CopyCop.Gui.ViewModels;

internal static class FileImportTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        const string sample = "äöü €\r\n\tQuellcode 😀";
        foreach (var encoding in new Encoding[]
        {
            new UTF8Encoding(false, true), new UTF8Encoding(true, true),
            new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true),
            new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
        })
        {
            using var stream = new ShortReadStream([.. encoding.GetPreamble(), .. encoding.GetBytes(sample)]);
            check(await TextFileReader.ReadAsync(stream) == sample,
                $"file import preserves {encoding.WebName}, BOM {encoding.GetPreamble().Length}");
            check(stream.CanRead, "file reader leaves stream ownership with caller");
        }

        using (var empty = new MemoryStream())
            check(await TextFileReader.ReadAsync(empty) == "", "empty file");
        using (var bomOnly = new MemoryStream(Encoding.UTF8.GetPreamble()))
            check(await TextFileReader.ReadAsync(bomOnly) == "", "BOM-only file");

        foreach (var invalid in new byte[][]
        {
            [0xC3, 0x28], [0xEF, 0xBB, 0xBF, 0xFF], // Invalid UTF-8 with and without BOM.
            [0xFF, 0xFE, 0x61], [0xFF, 0xFE, 0x00, 0xD8], // Truncation and lone surrogate.
            [0x00, 0x00, 0xFE, 0xFF, 0x00, 0x11, 0x00, 0x00], // Out-of-range UTF-32.
            [0x41, 0x00, 0x42], [0x1B, 0x41], // Binary NUL and terminal escape.
        })
        {
            using var stream = new MemoryStream(invalid);
            await ExpectAsync<InvalidDataException>(() => TextFileReader.ReadAsync(stream), check,
                "reject invalid encoding or binary control bytes");
        }

        var bytes = Enumerable.Repeat((byte)'a', TextFileReader.MaximumFileBytes).ToArray();
        using (var atLimit = new MemoryStream(bytes))
            check((await TextFileReader.ReadAsync(atLimit)).Length == bytes.Length, "file exact import limit");
        using (var overLimit = new MemoryStream([.. bytes, (byte)'b']))
            await ExpectAsync<InvalidDataException>(() => TextFileReader.ReadAsync(overLimit), check,
                "reject file above import limit");
        using (var cancelled = new MemoryStream([0x41]))
            await ExpectAsync<OperationCanceledException>(
                () => TextFileReader.ReadAsync(cancelled, new CancellationToken(true)), check,
                "cancel file read");

        using (var oversized = new MemoryStream(Encoding.UTF8.GetBytes(
            new string('ä', TextCapacity.FirmwareMaximumBytes))))
        {
            var content = await TextFileReader.ReadAsync(oversized);
            var assessment = TextCapacity.Assess(content, false);
            var parts = TextSplitter.Split(assessment.Analysis.Text, assessment.MaximumBytes);
            check(!assessment.FitsCapacity && string.Concat(parts.Select(part => part.Text)) == content
                && parts.All(part => part.Utf8Bytes <= TextCapacity.FirmwareMaximumBytes),
                "import above device capacity remains splittable without loss");
        }

        await TestViewModelAsync(check);
    }

    private static async Task TestViewModelAsync(Action<bool, string> check)
    {
        var pending = new TaskCompletionSource<(string Name, string Text)?>();
        await using var model = new MainWindowViewModel(() => Task.FromResult<string?>(null),
            _ => pending.Task);
        model.Text = new string('x', TextCapacity.FirmwareMaximumBytes + 1);
        model.SplitCommand.Execute(null);
        var originalPart = model.SelectedPart;

        var import = ExecuteAsync(model.OpenFileCommand);
        check(!model.IsIdle && !model.OpenFileCommand.CanExecute(null)
            && !model.PasteClipboardCommand.CanExecute(null) && !model.SplitCommand.CanExecute(null)
            && !model.CanSend, "import prevents conflicting actions");
        pending.SetResult(null);
        await import;
        check(model.SelectedPart == originalPart && model.HasParts && model.IsIdle,
            "picker cancellation preserves editor and parts");

        pending = new TaskCompletionSource<(string Name, string Text)?>();
        import = ExecuteAsync(model.OpenFileCommand);
        pending.SetException(new IOException("Test: Zugriff verweigert"));
        await import;
        check(model.SelectedPart == originalPart && model.HasParts && model.IsIdle
            && model.ActivityText.Contains("Zugriff verweigert"), "read failure preserves content and reports error");

        pending = new TaskCompletionSource<(string Name, string Text)?>();
        import = ExecuteAsync(model.OpenFileCommand);
        pending.SetResult(("gleich.txt", model.Text));
        await import;
        check(!model.HasParts && model.SelectedPart is null && model.NeedsSplit,
            "reimport invalidates previous part selection even for identical text");

        pending = new TaskCompletionSource<(string Name, string Text)?>();
        import = ExecuteAsync(model.OpenFileCommand);
        pending.SetResult(("unicode.txt", "ä😀"));
        await import;
        check(model.Text == "ä😀" && model.HasBlockingUnsupported
            && model.ActivityText.Contains("unicode.txt"), "file import uses existing Unicode validation");
        model.ReplaceUnsupported = true;
        check(!model.HasBlockingUnsupported, "import supports explicit unsupported character replacement");

        pending = new TaskCompletionSource<(string Name, string Text)?>();
        import = ExecuteAsync(model.OpenFileCommand);
        pending.SetResult(("leer.txt", ""));
        await import;
        check(model.Text == "" && !model.CanSend && model.ActivityText.Contains("ist leer"),
            "empty file clears editor and cannot be sent");

        var clipboard = new TaskCompletionSource<string?>();
        await using var clipboardModel = new MainWindowViewModel(() => clipboard.Task,
            _ => Task.FromResult<(string Name, string Text)?>(null));
        var paste = ExecuteAsync(clipboardModel.PasteClipboardCommand);
        check(!clipboardModel.OpenFileCommand.CanExecute(null),
            "pending clipboard read prevents overlapping file import");
        clipboard.SetResult("Zwischenablage");
        await paste;
        check(clipboardModel.Text == "Zwischenablage" && clipboardModel.OpenFileCommand.CanExecute(null),
            "clipboard completion re-enables file import");
    }

    private static async Task ExecuteAsync(AsyncRelayCommand command)
    {
        var finished = new TaskCompletionSource();
        void Changed(object? sender, EventArgs args)
        {
            if (command.CanExecute(null)) finished.TrySetResult();
        }
        command.CanExecuteChanged += Changed;
        try
        {
            command.Execute(null);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { command.CanExecuteChanged -= Changed; }
    }

    private static async Task ExpectAsync<T>(Func<Task> action, Action<bool, string> check, string name)
        where T : Exception
    {
        try { await action(); check(false, name); }
        catch (T) { check(true, name); }
    }

    private sealed class ShortReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
    }
}
