using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using WifiSender.ViewModels;

namespace WifiSender.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel vm && vm.CanSendFiles())
        {
            vm.SendFilesCommand.Execute(this);
            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        if (e.Data.Contains(DataFormats.Files))
#pragma warning restore CS0618
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        if (!e.Data.Contains(DataFormats.Files)) return;

        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        if (files == null) return;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedFiles.Clear();
            foreach (var file in files)
            {
                if (file.TryGetLocalPath() is { } localPath)
                {
                    // If it's a directory, expand it recursively
                    if (System.IO.Directory.Exists(localPath))
                    {
                        try
                        {
                            var allFiles = System.IO.Directory.GetFiles(localPath, "*", System.IO.SearchOption.AllDirectories);
                            foreach (var f in allFiles)
                                vm.SelectedFiles.Add(f);
                        }
                        catch { }
                    }
                    else if (System.IO.File.Exists(localPath))
                    {
                        vm.SelectedFiles.Add(localPath);
                    }
                }
            }

            if (vm.SelectedFiles.Count > 0)
            {
                long totalSize = 0;
                foreach (var f in vm.SelectedFiles)
                {
                    if (System.IO.File.Exists(f))
                        totalSize += new System.IO.FileInfo(f).Length;
                }
                vm.Status = $"Dropped {vm.SelectedFiles.Count} file(s) ({MainWindowViewModel.FormatFileSize(totalSize)})";
                vm.SendFilesCommand.NotifyCanExecuteChanged();
            }
        }
    }
}
