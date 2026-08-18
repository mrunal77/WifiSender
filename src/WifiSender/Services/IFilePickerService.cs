using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace WifiSender.Services;

public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickFilesAsync(Window? window, CancellationToken cancellationToken = default);
    Task<string?> PickFolderAsync(Window? window, CancellationToken cancellationToken = default);
    Task<string?> PickDownloadFolderAsync(Window? window, CancellationToken cancellationToken = default);
}
