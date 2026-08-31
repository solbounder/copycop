using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
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
        });
        DataContext = viewModel;
        Opened += (_, _) => viewModel.Start();
        Closed += async (_, _) => await viewModel.DisposeAsync();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
