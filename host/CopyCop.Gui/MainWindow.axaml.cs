using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CopyCop.Core;
using CopyCop.Gui.ViewModels;

namespace CopyCop.Gui;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainWindowViewModel(async () =>
        {
            var clipboard = Clipboard;
            return clipboard is null ? null : await clipboard.TryGetTextAsync();
        }, ReadTextFileAsync, ReadBundleFilesAsync, SaveBundleAsync, ReadBundleFolderAsync);
        DataContext = viewModel;
        Opened += (_, _) => viewModel.Start();
        Closed += async (_, _) => await viewModel.DisposeAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task<(string Name, string Text)?> ReadTextFileAsync(CancellationToken cancellationToken)
    {
        if (!StorageProvider.CanOpen)
            throw new InvalidOperationException("Auf diesem System ist keine Dateiauswahl verfügbar.");

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Textdatei einlesen",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CopyCop, Text und Quellcode")
                {
                    Patterns = ["*.copycop", "*.txt", "*.md", "*.csv", "*.json", "*.xml", "*.yaml", "*.yml",
                        "*.log", "*.cs", "*.py", "*.js", "*.ts", "*.html", "*.css", "*.sh", "*.ps1"],
                },
                new FilePickerFileType("CopyCop-Pakete") { Patterns = ["*.copycop"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0) return null;

        using var file = files[0];
        await using var stream = await file.OpenReadAsync();
        var contents = await TextFileReader.ReadAsync(stream, cancellationToken);
        return (file.Name, contents);
    }

    private async Task<IReadOnlyList<BundleFile>?> ReadBundleFilesAsync(CancellationToken cancellationToken)
    {
        if (!StorageProvider.CanOpen)
            throw new InvalidOperationException("Auf diesem System ist keine Dateiauswahl verfügbar.");
        var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Dateien zur bestehenden Auswahl hinzufügen",
            AllowMultiple = true,
            FileTypeFilter = [FilePickerFileTypes.All],
        });
        try
        {
            if (selected.Count == 0) return null;
            if (selected.Count > CopyCopBundle.MaximumFiles)
                throw new InvalidDataException($"Maximal {CopyCopBundle.MaximumFiles} Dateien pro Paket.");
            var files = new List<BundleFile>();
            var remaining = CopyCopBundle.MaximumExpandedBytes;
            foreach (var file in selected)
            {
                await using var stream = await file.OpenReadAsync();
                var input = await CopyCopBundle.ReadFileAsync(file.Name, stream, remaining, cancellationToken);
                files.Add(input);
                remaining -= input.Data.Length;
            }
            return files;
        }
        finally { foreach (var file in selected) file.Dispose(); }
    }

    private async Task<IReadOnlyList<BundleFile>?> ReadBundleFolderAsync(CancellationToken cancellationToken)
    {
        if (!StorageProvider.CanPickFolder)
            throw new InvalidOperationException("Auf diesem System ist keine Ordnerauswahl verfügbar.");
        var selected = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Ordner zur bestehenden Auswahl hinzufügen",
            AllowMultiple = false,
        });
        if (selected.Count == 0) return null;
        using var root = selected[0];
        var files = new List<BundleFile>();
        var remaining = CopyCopBundle.MaximumExpandedBytes;
        var visited = new HashSet<string>();
        async Task Visit(IStorageFolder folder, string prefix)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyCopBundle.ValidateName(prefix);
            if (!visited.Add(folder.Path.ToString()) || visited.Count > 4096)
                throw new InvalidDataException("Der Ordner enthält eine Schleife oder zu viele Unterordner.");
            await foreach (var item in folder.GetItemsAsync())
            {
                using (item)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (item.TryGetLocalPath() is { } local && File.GetAttributes(local).HasFlag(FileAttributes.ReparsePoint))
                        throw new InvalidDataException($"Verknüpfungen im Ordner werden nicht übernommen: {item.Name}");
                    var name = prefix + "/" + item.Name;
                    CopyCopBundle.ValidateName(name);
                    if (item is IStorageFolder child) await Visit(child, name);
                    else if (item is IStorageFile file)
                    {
                        if (files.Count >= CopyCopBundle.MaximumFiles) throw new InvalidDataException("Maximal 256 Dateien pro Paket.");
                        await using var stream = await file.OpenReadAsync();
                        var source = await CopyCopBundle.ReadFileAsync(name, stream, remaining, cancellationToken);
                        files.Add(source);
                        remaining -= source.Data.Length;
                    }
                }
            }
        }
        await Visit(root, root.Name);
        if (files.Count == 0) throw new InvalidDataException("Der Ordner enthält keine Dateien.");
        return files;
    }

    private async Task<string?> SaveBundleAsync(string text, CancellationToken cancellationToken)
    {
        if (!StorageProvider.CanSave)
            throw new InvalidOperationException("Auf diesem System ist kein Speicherdialog verfügbar.");
        using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "CopyCop-Paket speichern",
            SuggestedFileName = "paket.copycop",
            DefaultExtension = "copycop",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("CopyCop-Paket") { Patterns = ["*.copycop"] }],
        });
        if (file is null) return null;
        cancellationToken.ThrowIfCancellationRequested();
        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek) stream.SetLength(0);
        var bytes = new System.Text.UTF8Encoding(false, true).GetBytes(text);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return file.Name;
    }
}
