using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace WifiSender.Services;

public sealed class FilePickerService : IFilePickerService
{
    private static readonly FolderPickerOpenOptions DownloadFolderOptions = new()
    {
        Title = "Select Download Folder",
        AllowMultiple = false
    };

    public async Task<IReadOnlyList<string>> PickFilesAsync(Window? window, CancellationToken cancellationToken = default)
    {
        if (window == null)
            return Array.Empty<string>();

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Files to Send",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        return files.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> PickFolderAsync(Window? window, CancellationToken cancellationToken = default)
    {
        if (window == null)
            return null;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder to Send",
            AllowMultiple = false
        });

        if (folders.Count == 0)
            return null;

        return folders[0].Path.LocalPath;
    }

    public async Task<string?> PickDownloadFolderAsync(Window? window, CancellationToken cancellationToken = default)
    {
        if (window == null)
            return null;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(DownloadFolderOptions);
        if (folders.Count == 0)
            return null;

        return folders[0].Path.LocalPath;
    }
}
