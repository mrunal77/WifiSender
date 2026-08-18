using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using WifiSender.Models;
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

        SendScrollViewer.ScrollChanged += OnScrollChanged;
        ReceiveScrollViewer.ScrollChanged += OnScrollChanged;
        MainTabControl.SelectionChanged += OnTabSelectionChanged;

        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm && !vm.IsReceiving)
            {
                vm.ToggleReceivingCommand.Execute(this);
            }
        };
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateNavBarScrollState();
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateNavBarScrollState();
    }

    private void UpdateNavBarScrollState()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var sv = MainTabControl.SelectedIndex == 0 ? SendScrollViewer : ReceiveScrollViewer;
            vm.IsNavBarScrolled = sv.Offset.Y > 0;
        }
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
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
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
        if (!e.DataTransfer.Formats.Contains(DataFormat.File)) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files == null || !files.Any()) return;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectedFiles.Clear();

            foreach (var file in files)
            {
                if (file.TryGetLocalPath() is { } localPath)
                {
                    if (System.IO.Directory.Exists(localPath))
                    {
                        try
                        {
                            var allFiles = System.IO.Directory.GetFiles(localPath, "*", System.IO.SearchOption.AllDirectories);
                            foreach (var f in allFiles)
                                vm.SelectedFiles.Add(new SelectedFileItem(f));
                        }
                        catch { }
                    }
                    else if (System.IO.File.Exists(localPath))
                    {
                        vm.SelectedFiles.Add(new SelectedFileItem(localPath));
                    }
                }
            }

            if (vm.SelectedFiles.Count > 0)
            {
                vm.Status = $"Dropped {vm.SelectedFilesSummary}";
                vm.ShowToast($"📥 Dropped {vm.SelectedFilesSummary}");
                vm.SendFilesCommand.NotifyCanExecuteChanged();
            }
        }
    }
}
