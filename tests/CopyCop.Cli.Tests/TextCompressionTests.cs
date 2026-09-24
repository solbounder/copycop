using System.Text;
using CopyCop.Core;
using CopyCop.Gui.ViewModels;

internal static class TextCompressionTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var source = string.Concat(Enumerable.Repeat("\uFEFFOriginal: „Grüße“ 😀 你好\r\n\treturn 42;\r\n", 1000));
        var preparation = new TextTransferPreparation();
        check(preparation.Prepare(source, false).Text == source, "text compression is opt-in");
        var packed = preparation.Prepare(source, true);
        check(packed.Error is null && packed.Text.Length < source.Length && packed.Hint.Contains("weniger"),
            "text mode reports actual smaller transfer with compression");
        var file = CopyCopBundle.Read(packed.Text).Single();
        check(file.Name == "text.txt" && file.Data.SequenceEqual(Encoding.UTF8.GetBytes(source)),
            "compressed text preserves original Unicode, BOM, quotes, CRLF and tabs");
        check(TextCapacity.Assess(packed.Text, false).CanTransfer,
            "Unicode original has a keyboard-safe prepared transfer");
        check(ReferenceEquals(packed, preparation.Prepare(source, true)), "unchanged text reuses its prepared package");
        check(preparation.Prepare(source, false).Text == source, "turning compression off restores direct source");
        var shortText = preparation.Prepare("abc", true);
        check(shortText.Hint.Contains("mehr") && CopyCopBundle.Read(shortText.Text)[0].Data.SequenceEqual("abc"u8.ToArray()),
            "short text reports overhead while preserving the selected packet mode");
        check(preparation.Prepare(packed.Text, true).Text == packed.Text, "existing packages are never double wrapped");
        check(preparation.Prepare("", true).Text == "", "empty editor does not become a sendable package");
        var invalid = preparation.Prepare("COPYCOP/1 invalid", true);
        check(invalid.Error is not null && invalid.Text == "", "invalid package fails closed in compression mode");
        var brokenUnicode = preparation.Prepare("\ud800", true);
        check(brokenUnicode.Error is not null && brokenUnicode.Text == "", "unpaired surrogate is not silently replaced");
        var tooLarge = preparation.Prepare(new string('x', CopyCopBundle.MaximumExpandedBytes + 1), true);
        check(tooLarge.Error is not null && tooLarge.Text == "", "text compression enforces source limit");
        check(preparation.Prepare("recovered", false).Text == "recovered", "switching back recovers after preparation error");

        string? saved = null;
        var clipboard = source;
        await using var model = new MainWindowViewModel(() => Task.FromResult<string?>(clipboard),
            _ => Task.FromResult<(string, string)?>(null),
            saveBundle: (text, _) => { saved = text; return Task.FromResult<string?>("test.copycop"); });
        check(!model.CompressText, "desktop starts in direct text mode");
        await ExecuteAsync(model.PasteClipboardCommand);
        check(model.Text == source && model.HasBlockingUnsupported, "default paste uses ordinary keyboard validation");
        model.CompressBundle = false;
        model.CompressText = true;
        check(model.Text == source && !model.HasBlockingUnsupported && !model.NeedsSplit
            && model.TextCompressionHint.Contains("weniger") && model.Duration10 != "—",
            "toggle preserves editable source and assesses the package including typing time");
        await ExecuteAsync(model.SaveBundleCommand);
        check(saved == packed.Text, "save uses the same compressed payload despite file compression setting");
        clipboard += "\r\nneu 😀";
        await ExecuteAsync(model.PasteClipboardCommand);
        await ExecuteAsync(model.SaveBundleCommand);
        check(model.Text == clipboard && Encoding.UTF8.GetString(CopyCopBundle.Read(saved!)[0].Data) == clipboard,
            "new paste recomputes payload rather than transferring stale text");
        model.CompressText = false;
        check(model.Text == clipboard && model.HasBlockingUnsupported, "disabling compression restores direct-mode validation");

        var random = new byte[250_000];
        new Random(924).NextBytes(random);
        var multipartSource = Convert.ToBase64String(random);
        model.Text = multipartSource;
        model.CompressText = true;
        check(model.NeedsSplit && model.SplitCommand.CanExecute(null), "oversized compressed payload can be split");
        model.SplitCommand.Execute(null);
        var restored = CopyCopBundle.Read(string.Concat(model.Parts.Select(part => part.Text))).Single();
        check(model.Parts.Count > 1 && model.Parts.All(part => part.Utf8Bytes <= TextCapacity.FirmwareMaximumBytes)
            && Encoding.UTF8.GetString(restored.Data) == multipartSource,
            "split and transfer parts contain the compressed current source");
        model.CompressText = false;
        check(model.Parts.Count == 0 && model.SelectedPart is null && model.Text == multipartSource,
            "changing transfer mode invalidates old part selection");
        model.CompressText = true;
        model.Text = "COPYCOP/1 invalid";
        check(model.CapacityHeadline == "Kompression fehlgeschlagen" && !model.SaveBundleCommand.CanExecute(null)
            && !model.SplitCommand.CanExecute(null) && !model.CanSend,
            "preparation failure disables save, split and send without losing source");
    }

    private static async Task ExecuteAsync(AsyncRelayCommand command)
    {
        var finished = new TaskCompletionSource();
        void Changed(object? sender, EventArgs args) { if (command.CanExecute(null)) finished.TrySetResult(); }
        command.CanExecuteChanged += Changed;
        try { command.Execute(null); await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { command.CanExecuteChanged -= Changed; }
    }
}
