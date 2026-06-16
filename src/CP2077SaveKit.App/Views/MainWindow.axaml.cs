using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CP2077SaveKit.App.ViewModels;

namespace CP2077SaveKit.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        this.FindControl<Button>("OpenBtn")!.Click += OnOpen;
        this.FindControl<Button>("SaveAsBtn")!.Click += OnSaveAs;
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        var start = await StartFolder();
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Cyberpunk 2077 save (sav.dat)",
            AllowMultiple = false,
            SuggestedStartLocation = start,
            FileTypeFilter = new[] { new FilePickerFileType("CP2077 save") { Patterns = new[] { "sav.dat", "*.dat" } } }
        });
        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) Vm.LoadFromPath(path);
    }

    private async void OnSaveAs(object? sender, RoutedEventArgs e)
    {
        var start = await StartFolder();
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save edited sav.dat",
            SuggestedFileName = "sav.dat",
            SuggestedStartLocation = start,
            DefaultExtension = "dat"
        });
        var path = file?.TryGetLocalPath();
        if (path is not null) Vm.SaveToPath(path);
    }

    private async System.Threading.Tasks.Task<IStorageFolder?> StartFolder()
    {
        try
        {
            var dir = MainWindowViewModel.DefaultSavesDir;
            if (Directory.Exists(dir))
                return await StorageProvider.TryGetFolderFromPathAsync(dir);
        }
        catch { /* ignore */ }
        return null;
    }
}
