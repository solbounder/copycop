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
        }, ReadTextFileAsync);
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
                new FilePickerFileType("Text und Quellcode")
                {
                    Patterns = ["*.txt", "*.md", "*.csv", "*.json", "*.xml", "*.yaml", "*.yml",
                        "*.log", "*.cs", "*.py", "*.js", "*.ts", "*.html", "*.css", "*.sh", "*.ps1"],
                },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0) return null;

        using var file = files[0];
        await using var stream = await file.OpenReadAsync();
        var contents = await TextFileReader.ReadAsync(stream, cancellationToken);
        return (file.Name, contents);
    }
}
