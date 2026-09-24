using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Media;
using Avalonia.Threading;
using CopyCop.Core;
using CopyCop.Hid;

namespace CopyCop.Gui.ViewModels;

public sealed class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#63E6A5"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#70A5FF"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#FFC66D"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#FF7A90"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8D9AB5"));

    private readonly Func<Task<string?>> readClipboard;
    private readonly Func<CancellationToken, Task<(string Name, string Text)?>> readTextFile;
    private readonly Func<CancellationToken, Task<IReadOnlyList<BundleFile>?>>? readBundleFiles;
    private readonly Func<CancellationToken, Task<IReadOnlyList<BundleFile>?>>? readBundleFolder;
    private readonly BundleFileSelection fileSelection = new();
    private BundleFile? selectedBundleFile;
    private bool selectionNeedsBuild;
    private readonly Func<string, CancellationToken, Task<string?>>? saveBundle;
    private bool compressBundle = true;
    private bool isReadingInput;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? transferCancellation;
    private Task? connectionTask;
    private TransferClient? client;
    private string text = string.Empty;
    private bool replaceUnsupported;
    private TextAssessment assessment;
    private TextPart? selectedPart;
    private string connectionText = "LOAD-Gerät wird gesucht";
    private string connectionDetail = "C beim Einstecken gedrückt halten";
    private IBrush connectionBrush = Blue;
    private string activityText = "Bereit für deine Zwischenablage.";
    private IBrush activityBrush = Muted;
    private bool isConnected;
    private bool isBusy;
    private double transferProgress;
    private uint storedBytes;
    private TypingWorkload typingWorkload;
    private bool hasTypingEstimate;

    public MainWindowViewModel(Func<Task<string?>> readClipboard,
        Func<CancellationToken, Task<(string Name, string Text)?>> readTextFile,
        Func<CancellationToken, Task<IReadOnlyList<BundleFile>?>>? readBundleFiles = null,
        Func<string, CancellationToken, Task<string?>>? saveBundle = null,
        Func<CancellationToken, Task<IReadOnlyList<BundleFile>?>>? readBundleFolder = null)
    {
        this.readClipboard = readClipboard;
        this.readTextFile = readTextFile;
        this.readBundleFiles = readBundleFiles;
        this.readBundleFolder = readBundleFolder;
        this.saveBundle = saveBundle;
        assessment = TextCapacity.Assess(string.Empty, false);
        UpdateTypingEstimate();
        PasteClipboardCommand = new AsyncRelayCommand(PasteClipboardAsync, () => IsIdle);
        OpenFileCommand = new AsyncRelayCommand(OpenFileAsync, () => IsIdle);
        BundleFilesCommand = new AsyncRelayCommand(() => AddBundleFilesAsync(this.readBundleFiles),
            () => IsIdle && this.readBundleFiles is not null);
        BundleFolderCommand = new AsyncRelayCommand(() => AddBundleFilesAsync(this.readBundleFolder),
            () => IsIdle && this.readBundleFolder is not null);
        BuildBundleCommand = new AsyncRelayCommand(BuildBundleAsync, () => IsIdle && HasBundleFiles);
        RemoveBundleFileCommand = new RelayCommand(() =>
        {
            if (SelectedBundleFile is { } file && fileSelection.Remove(file.Name)) RefreshFileSelection(changed: true);
        }, () => IsIdle && SelectedBundleFile is not null);
        ClearBundleFilesCommand = new RelayCommand(() =>
        {
            fileSelection.Clear();
            RefreshFileSelection(changed: true);
        }, () => IsIdle && HasBundleFiles);
        SaveBundleCommand = new AsyncRelayCommand(SaveBundleAsync,
            () => IsIdle && !selectionNeedsBuild && Text.Length > 0 && this.saveBundle is not null);
        SplitCommand = new RelayCommand(SplitText, () => IsIdle && NeedsSplit && !HasBlockingUnsupported);
        SendCommand = new AsyncRelayCommand(SendSelectedAsync, () => CanSend);
        CancelCommand = new RelayCommand(CancelTransfer, () => IsBusy);
    }

    public ObservableCollection<TextPart> Parts { get; } = [];
    public ObservableCollection<BundleFile> BundleFiles { get; } = [];
    public bool HasBundleFiles => BundleFiles.Count > 0;
    public string BundleSelectionSummary => $"{BundleFiles.Count} Dateien · {fileSelection.TotalBytes:N0} Bytes"
        + (selectionNeedsBuild ? " · Paket neu erstellen" : "");
    public BundleFile? SelectedBundleFile
    {
        get => selectedBundleFile;
        set { if (SetProperty(ref selectedBundleFile, value)) NotifyCommands(); }
    }
    public AsyncRelayCommand PasteClipboardCommand { get; }
    public AsyncRelayCommand OpenFileCommand { get; }
    public AsyncRelayCommand BundleFilesCommand { get; }
    public AsyncRelayCommand BundleFolderCommand { get; }
    public AsyncRelayCommand BuildBundleCommand { get; }
    public RelayCommand RemoveBundleFileCommand { get; }
    public RelayCommand ClearBundleFilesCommand { get; }
    public AsyncRelayCommand SaveBundleCommand { get; }
    public RelayCommand SplitCommand { get; }
    public AsyncRelayCommand SendCommand { get; }
    public RelayCommand CancelCommand { get; }

    public bool CompressBundle
    {
        get => compressBundle;
        set
        {
            if (SetProperty(ref compressBundle, value) && HasBundleFiles) RefreshFileSelection(changed: true);
        }
    }

    public string Text
    {
        get => text;
        set
        {
            if (!SetProperty(ref text, value ?? string.Empty)) return;
            Reassess(clearParts: true);
        }
    }

    public bool ReplaceUnsupported
    {
        get => replaceUnsupported;
        set
        {
            if (!SetProperty(ref replaceUnsupported, value)) return;
            Reassess(clearParts: true);
        }
    }

    public TextPart? SelectedPart
    {
        get => selectedPart;
        set
        {
            if (!SetProperty(ref selectedPart, value)) return;
            OnPropertyChanged(nameof(SelectedPartDetail));
            NotifyCommands();
        }
    }

    public string ConnectionText
    {
        get => connectionText;
        private set => SetProperty(ref connectionText, value);
    }

    public string ConnectionDetail
    {
        get => connectionDetail;
        private set => SetProperty(ref connectionDetail, value);
    }

    public IBrush ConnectionBrush
    {
        get => connectionBrush;
        private set => SetProperty(ref connectionBrush, value);
    }

    public string ActivityText
    {
        get => activityText;
        private set => SetProperty(ref activityText, value);
    }

    public IBrush ActivityBrush
    {
        get => activityBrush;
        private set => SetProperty(ref activityBrush, value);
    }

    public bool IsConnected
    {
        get => isConnected;
        private set
        {
            if (!SetProperty(ref isConnected, value)) return;
            NotifyCommands();
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            NotifyCommands();
        }
    }

    public bool IsIdle => !IsBusy && !isReadingInput;

    public double TransferProgress
    {
        get => transferProgress;
        private set => SetProperty(ref transferProgress, value);
    }

    public string CapacityHeadline => !assessment.HasText
        ? "Noch kein Text"
        : HasBlockingUnsupported
            ? "Nicht unterstützte Zeichen"
            : assessment.FitsCapacity ? "Passt vollständig" : "Text ist zu groß";

    public IBrush CapacityBrush => !assessment.HasText ? Muted
        : HasBlockingUnsupported ? Red
        : assessment.FitsCapacity ? Green : Amber;

    public string CharacterText =>
        $"{assessment.Analysis.OriginalCharacterCount:N0} Zeichen";

    public string ByteText =>
        $"{assessment.Utf8Bytes:N0} / {assessment.MaximumBytes:N0} UTF-8-Bytes";

    public string RemainingText
    {
        get
        {
            var difference = assessment.MaximumBytes - assessment.Utf8Bytes;
            if (difference >= 0) return $"{difference:N0} Bytes frei";
            var excess = -difference;
            return excess == 1
                ? $"1 Byte zu viel · {assessment.RequiredParts:N0} Teile"
                : $"{excess:N0} Bytes zu viel · {assessment.RequiredParts:N0} Teile";
        }
    }

    public double UsagePercent => assessment.UsagePercent;
    public bool HasUnsupported => assessment.HasUnsupported;
    public bool HasBlockingUnsupported => assessment.HasUnsupported && !ReplaceUnsupported;
    public bool NeedsSplit => assessment.HasText && !assessment.FitsCapacity;
    public bool HasParts => Parts.Count > 0;
    public bool ShowProgress => IsBusy && TransferProgress > 0;

    public string TypingEstimateHint => !assessment.HasText
        ? "Text eingeben, um die Tippdauer zu berechnen."
        : HasBlockingUnsupported
            ? "Die Dauer ist nach Ersetzen oder Entfernen unbekannter Zeichen verfügbar."
            : $"{typingWorkload.StrokeCount:N0} Tastenfolgen · AltGr inklusive · ohne manuelle Pausen";

    public string Duration10 => EstimateAt(10);
    public string Duration25 => EstimateAt(25);
    public string Duration50 => EstimateAt(50);
    public string Duration100 => EstimateAt(100);
    public string Duration250 => EstimateAt(250);
    public string Duration500 => EstimateAt(500);
    public string Duration750 => EstimateAt(750);
    public string Duration1000 => EstimateAt(1000);

    public string UnsupportedText
    {
        get
        {
            if (!assessment.HasUnsupported) return "Alle Zeichen sind auf deutscher QWERTZ-Tastatur darstellbar.";
            var examples = string.Join("  ", assessment.Analysis.Unsupported.Take(5)
                .Select(item => $"U+{item.CodePoint:X4} {item.Display}"));
            var count = assessment.Analysis.Unsupported.Count;
            var action = count == 1
                ? ReplaceUnsupported ? "wird durch ? ersetzt" : "blockiert die Übertragung"
                : ReplaceUnsupported ? "werden durch ? ersetzt" : "blockieren die Übertragung";
            return $"{count:N0} Zeichen {action}:  {examples}";
        }
    }

    public string SplitSummary => NeedsSplit
        ? $"Verlustfrei in {assessment.RequiredParts:N0} Teile aufteilen"
        : "Text passt in einen Teil";

    public string SelectedPartDetail => SelectedPart is null
        ? "Wähle nach dem Aufteilen den Teil, der gespeichert werden soll."
        : $"{SelectedPart} wird gespeichert. Die übrigen Teile bleiben in der App.";

    public string StoredText => IsConnected
        ? $"Auf dem Gerät: {storedBytes:N0} Bytes"
        : "Gerätespeicher wird nach Verbindung angezeigt";

    public string SendButtonText => SelectedPart is null
        ? "Text auf CopyCop speichern"
        : $"Teil {SelectedPart.Number} auf CopyCop speichern";

    public bool CanSend => IsConnected && IsIdle && !selectionNeedsBuild && assessment.HasText
        && !HasBlockingUnsupported
        && (assessment.FitsCapacity || SelectedPart is not null);

    public void Start()
    {
        connectionTask ??= ConnectionLoopAsync();
    }

    private void Reassess(bool clearParts)
    {
        assessment = TextCapacity.Assess(Text, ReplaceUnsupported);
        UpdateTypingEstimate();
        if (clearParts)
        {
            Parts.Clear();
            selectedPart = null;
            OnPropertyChanged(nameof(SelectedPart));
            OnPropertyChanged(nameof(SelectedPartDetail));
        }
        RaiseAssessmentProperties();
    }

    private void RaiseAssessmentProperties()
    {
        OnPropertyChanged(nameof(CapacityHeadline));
        OnPropertyChanged(nameof(CapacityBrush));
        OnPropertyChanged(nameof(CharacterText));
        OnPropertyChanged(nameof(ByteText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(UsagePercent));
        OnPropertyChanged(nameof(HasUnsupported));
        OnPropertyChanged(nameof(HasBlockingUnsupported));
        OnPropertyChanged(nameof(NeedsSplit));
        OnPropertyChanged(nameof(HasParts));
        OnPropertyChanged(nameof(UnsupportedText));
        OnPropertyChanged(nameof(SplitSummary));
        OnPropertyChanged(nameof(SendButtonText));
        OnPropertyChanged(nameof(TypingEstimateHint));
        OnPropertyChanged(nameof(Duration10));
        OnPropertyChanged(nameof(Duration25));
        OnPropertyChanged(nameof(Duration50));
        OnPropertyChanged(nameof(Duration100));
        OnPropertyChanged(nameof(Duration250));
        OnPropertyChanged(nameof(Duration500));
        OnPropertyChanged(nameof(Duration750));
        OnPropertyChanged(nameof(Duration1000));
        NotifyCommands();
    }

    private void UpdateTypingEstimate()
    {
        hasTypingEstimate = assessment.HasText && !assessment.HasBlockingUnsupported;
        typingWorkload = hasTypingEstimate
            ? TypingDurationEstimator.Analyze(assessment.Analysis.Text)
            : default;
    }

    private string EstimateAt(int delayMilliseconds) => hasTypingEstimate
        ? TypingDurationEstimator.Format(typingWorkload.Estimate(delayMilliseconds))
        : "—";

    private void SplitText()
    {
        Parts.Clear();
        foreach (var part in TextSplitter.Split(assessment.Analysis.Text, assessment.MaximumBytes))
            Parts.Add(part);
        SelectedPart = Parts.FirstOrDefault();
        OnPropertyChanged(nameof(HasParts));
        ActivityText = $"{Parts.Count:N0} Teile vorbereitet. Wähle einen Teil zum Speichern.";
        ActivityBrush = Blue;
    }

    private async Task OpenFileAsync()
    {
        isReadingInput = true;
        OnPropertyChanged(nameof(IsIdle));
        NotifyCommands();
        try
        {
            var file = await readTextFile(lifetime.Token);
            if (file is null || lifetime.IsCancellationRequested) return;
            IReadOnlyList<BundleFile> loadedFiles = [];
            if (Path.GetExtension(file.Value.Name).Equals(".copycop", StringComparison.OrdinalIgnoreCase)
                || CopyCopBundle.LooksLikeBundle(file.Value.Text))
                loadedFiles = await Task.Run(() => CopyCopBundle.Read(file.Value.Text), lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            fileSelection.Replace(loadedFiles);
            RefreshFileSelection(changed: false);
            // Invalidate an old part selection even when the new file has identical content.
            if (Text == file.Value.Text) Reassess(clearParts: true);
            else Text = file.Value.Text;
            ActivityText = !assessment.HasText
                ? $"„{file.Value.Name}“ ist leer."
                : HasBlockingUnsupported
                    ? $"„{file.Value.Name}“ eingelesen. Bitte nicht unterstützte Zeichen prüfen."
                    : assessment.FitsCapacity
                        ? $"„{file.Value.Name}“ eingelesen und geprüft. Bereit zum Speichern."
                        : $"„{file.Value.Name}“ eingelesen. Bitte in {assessment.RequiredParts:N0} Teile aufteilen.";
            ActivityBrush = assessment.CanTransfer ? Green : Amber;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ActivityText = $"Datei konnte nicht eingelesen werden: {exception.Message}";
            ActivityBrush = Red;
        }
        finally
        {
            isReadingInput = false;
            OnPropertyChanged(nameof(IsIdle));
            NotifyCommands();
        }
    }

    private void RefreshFileSelection(bool changed)
    {
        var selectedName = selectedBundleFile?.Name;
        BundleFiles.Clear();
        foreach (var file in fileSelection.Files) BundleFiles.Add(file);
        selectedBundleFile = BundleFiles.FirstOrDefault(file => file.Name == selectedName) ?? BundleFiles.FirstOrDefault();
        selectionNeedsBuild = changed && HasBundleFiles;
        if (changed && CopyCopBundle.LooksLikeBundle(Text)) Text = string.Empty;
        OnPropertyChanged(nameof(HasBundleFiles));
        OnPropertyChanged(nameof(SelectedBundleFile));
        OnPropertyChanged(nameof(BundleSelectionSummary));
        NotifyCommands();
    }

    private async Task AddBundleFilesAsync(Func<CancellationToken, Task<IReadOnlyList<BundleFile>?>>? picker)
    {
        if (picker is null) return;
        isReadingInput = true;
        OnPropertyChanged(nameof(IsIdle));
        NotifyCommands();
        try
        {
            var files = await picker(lifetime.Token);
            if (files is null || lifetime.IsCancellationRequested) return;
            var added = fileSelection.Add(files);
            if (added > 0) RefreshFileSelection(changed: true);
            ActivityText = added == 0 ? "Die ausgewählten Dateien sind bereits in der Liste."
                : $"{added} Dateien hinzugefügt. Die bisherigen bleiben erhalten. Jetzt Paket erstellen.";
            ActivityBrush = Blue;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ActivityText = $"Dateien konnten nicht hinzugefügt werden: {exception.Message}";
            ActivityBrush = Red;
        }
        finally
        {
            isReadingInput = false;
            OnPropertyChanged(nameof(IsIdle));
            NotifyCommands();
        }
    }

    private async Task BuildBundleAsync()
    {
        isReadingInput = true;
        OnPropertyChanged(nameof(IsIdle));
        NotifyCommands();
        try
        {
            var files = fileSelection.Files.ToArray();
            var compress = CompressBundle;
            var bundle = await Task.Run(() => CopyCopBundle.Create(files, compress), lifetime.Token);
            if (lifetime.IsCancellationRequested) return;
            RefreshFileSelection(changed: false);
            if (Text == bundle.Text) Reassess(clearParts: true);
            else Text = bundle.Text;
            if (NeedsSplit) SplitText();
            var saved = bundle.UncompressedArchiveBytes - bundle.ArchiveBytes;
            ActivityText = $"{bundle.FileCount} Dateien gebündelt · {bundle.OriginalBytes:N0} Original-Bytes · "
                + $"{Text.Length:N0} Tippzeichen · {saved:N0} ZIP-Bytes durch Kompression gespart. "
                + "Als .copycop speichern oder auf das Gerät laden. Am Ziel mit copycop.html entpacken.";
            ActivityBrush = Green;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ActivityText = $"Dateien konnten nicht gebündelt werden: {exception.Message}";
            ActivityBrush = Red;
        }
        finally
        {
            isReadingInput = false;
            OnPropertyChanged(nameof(IsIdle));
            NotifyCommands();
        }
    }

    private async Task SaveBundleAsync()
    {
        if (saveBundle is null) return;
        isReadingInput = true;
        OnPropertyChanged(nameof(IsIdle));
        NotifyCommands();
        try
        {
            var source = Text;
            var compress = CompressBundle;
            var contents = await Task.Run(() => CopyCopBundle.PrepareForSave(source, compress), lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            var name = await saveBundle(contents, lifetime.Token);
            if (name is null || lifetime.IsCancellationRequested) return;
            ActivityText = $"„{name}“ gespeichert. Das vollständige Paket kann später wieder geöffnet werden.";
            ActivityBrush = Green;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ActivityText = $"Paket konnte nicht gespeichert werden: {exception.Message}";
            ActivityBrush = Red;
        }
        finally
        {
            isReadingInput = false;
            OnPropertyChanged(nameof(IsIdle));
            NotifyCommands();
        }
    }

    private async Task<bool> PasteClipboardAsync()
    {
        isReadingInput = true;
        OnPropertyChanged(nameof(IsIdle));
        NotifyCommands();
        try
        {
            var clipboard = await readClipboard();
            if (clipboard is null)
            {
                ActivityText = "Die Zwischenablage enthält keinen Text.";
                ActivityBrush = Amber;
                return false;
            }
            Text = clipboard;
            fileSelection.Clear();
            RefreshFileSelection(changed: false);
            ActivityText = assessment.FitsCapacity
                ? "Zwischenablage eingefügt und geprüft."
                : $"Zwischenablage benötigt {assessment.RequiredParts:N0} Teile.";
            ActivityBrush = assessment.FitsCapacity ? Green : Amber;
            return true;
        }
        catch (Exception exception)
        {
            ActivityText = $"Zwischenablage konnte nicht gelesen werden: {exception.Message}";
            ActivityBrush = Red;
            return false;
        }
        finally
        {
            isReadingInput = false;
            OnPropertyChanged(nameof(IsIdle));
            NotifyCommands();
        }
    }

    private async Task HandleHardwareCopyAsync()
    {
        if (!IsIdle || !await PasteClipboardAsync()) return;
        if (assessment.CanTransfer) await SendTextAsync(assessment.Analysis.Text);
        else if (NeedsSplit)
        {
            ActivityText = "C erkannt: Text ist zu groß. Bitte aufteilen und einen Teil wählen.";
            ActivityBrush = Amber;
        }
    }

    private Task SendSelectedAsync()
    {
        var value = SelectedPart?.Text ?? assessment.Analysis.Text;
        return SendTextAsync(value);
    }

    private async Task SendTextAsync(string value)
    {
        var activeClient = client;
        if (activeClient is null || IsBusy) return;

        transferCancellation?.Dispose();
        transferCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        IsBusy = true;
        TransferProgress = 0;
        OnPropertyChanged(nameof(ShowProgress));
        ActivityText = "Übertragung läuft …";
        ActivityBrush = Blue;

        try
        {
            var utf8 = Encoding.UTF8.GetBytes(value);
            await activeClient.TransferAsync(utf8, (sent, total) =>
            {
                Dispatcher.UIThread.Post(() =>
                    TransferProgress = total == 0 ? 100 : sent * 100d / total);
            }, transferCancellation.Token);
            storedBytes = (uint)utf8.Length;
            OnPropertyChanged(nameof(StoredText));
            TransferProgress = 100;
            ActivityText = $"Erfolgreich verifiziert und gespeichert · {utf8.Length:N0} Bytes";
            ActivityBrush = Green;
        }
        catch (OperationCanceledException) when (transferCancellation.IsCancellationRequested)
        {
            ActivityText = "Übertragung abgebrochen.";
            ActivityBrush = Amber;
        }
        catch (Exception exception)
        {
            ActivityText = $"Übertragung fehlgeschlagen: {exception.Message}";
            ActivityBrush = Red;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ShowProgress));
        }
    }

    private void CancelTransfer() => transferCancellation?.Cancel();

    private async Task ConnectionLoopAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                IsConnected = false;
                ConnectionText = "LOAD-Gerät wird gesucht";
                ConnectionDetail = "Mittlere Taste halten und CopyCop einstecken";
                ConnectionBrush = Blue;

                var device = await CopyCopDevice.WaitForLoadDeviceAsync(
                    lifetime.Token,
                    issue => Dispatcher.UIThread.Post(() =>
                    {
                        if (!string.IsNullOrWhiteSpace(issue)) ConnectionDetail = issue;
                    }));
                await using var connectedClient = new TransferClient(device);
                await connectedClient.HelloAsync(lifetime.Token);
                var info = await connectedClient.GetInfoAsync(lifetime.Token);
                client = connectedClient;
                storedBytes = info.StoredLength;
                IsConnected = true;
                ConnectionText = "CopyCop verbunden";
                ConnectionDetail = "Blauer LOAD-Modus · C übernimmt die Zwischenablage";
                ConnectionBrush = Green;
                OnPropertyChanged(nameof(StoredText));
                NotifyCommands();

                while (!lifetime.IsCancellationRequested)
                {
                    await connectedClient.WaitForCopyAsync(lifetime.Token);
                    await HandleHardwareCopyAsync();
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
                break;
            }
            catch (System.Threading.Channels.ChannelClosedException)
            {
                ActivityText = "CopyCop wurde getrennt. LOAD-Gerät wird erneut gesucht …";
                ActivityBrush = Muted;
            }
            catch (Exception exception)
            {
                ActivityText = $"Verbindung getrennt: {exception.Message}";
                ActivityBrush = Amber;
            }
            finally
            {
                client = null;
                IsConnected = false;
                NotifyCommands();
            }

            try { await Task.Delay(700, lifetime.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void NotifyCommands()
    {
        PasteClipboardCommand.RaiseCanExecuteChanged();
        OpenFileCommand.RaiseCanExecuteChanged();
        BundleFilesCommand.RaiseCanExecuteChanged();
        BundleFolderCommand.RaiseCanExecuteChanged();
        BuildBundleCommand.RaiseCanExecuteChanged();
        RemoveBundleFileCommand.RaiseCanExecuteChanged();
        ClearBundleFilesCommand.RaiseCanExecuteChanged();
        SaveBundleCommand.RaiseCanExecuteChanged();
        SplitCommand.RaiseCanExecuteChanged();
        SendCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(SendButtonText));
    }

    public async ValueTask DisposeAsync()
    {
        transferCancellation?.Cancel();
        lifetime.Cancel();
        if (connectionTask is not null)
        {
            try { await connectionTask; }
            catch (OperationCanceledException) { }
        }
        transferCancellation?.Dispose();
        lifetime.Dispose();
    }
}
