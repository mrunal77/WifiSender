using System;
using System.IO;
using Avalonia.Media;

namespace WifiSender.Models;

public sealed class SelectedFileItem
{
    public string FilePath { get; }
    public string FileName { get; }
    public long FileSize { get; }
    public string FormattedSize { get; }
    public bool IsDirectory { get; }
    public string ExtensionBadge { get; }
    public IBrush BadgeBackground { get; }
    public IBrush BadgeForeground { get; }
    public string IconResourceKey { get; }

    public SelectedFileItem(string path)
    {
        FilePath = path;
        IsDirectory = Directory.Exists(path);

        if (IsDirectory)
        {
            var dirInfo = new DirectoryInfo(path);
            FileName = dirInfo.Name;
            FileSize = 0;
            FormattedSize = "Folder";
            ExtensionBadge = "DIR";
            BadgeBackground = new SolidColorBrush(Color.Parse("#0284C7")); // Vivid Cyan Blue
            BadgeForeground = Brushes.White;
            IconResourceKey = "IconFolder";
        }
        else
        {
            var fileInfo = new FileInfo(path);
            FileName = fileInfo.Name;
            FileSize = fileInfo.Exists ? fileInfo.Length : 0;
            FormattedSize = FormatFileSize(FileSize);
            var ext = fileInfo.Extension.ToLowerInvariant();
            ExtensionBadge = string.IsNullOrEmpty(ext) ? "FILE" : ext.TrimStart('.').ToUpperInvariant();

            (BadgeBackground, IconResourceKey) = GetCategoryDetails(ext);
            BadgeForeground = Brushes.White;
        }
    }

    private static (IBrush Brush, string IconKey) GetCategoryDetails(string ext)
    {
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".svg" =>
                (new SolidColorBrush(Color.Parse("#EC4899")), "IconImage"), // Vibrant Pink/Rose

            ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm" or ".mp3" or ".wav" or ".flac" =>
                (new SolidColorBrush(Color.Parse("#8B5CF6")), "IconVideo"), // Vibrant Violet/Purple

            ".zip" or ".tar" or ".gz" or ".rar" or ".7z" or ".iso" =>
                (new SolidColorBrush(Color.Parse("#F59E0B")), "IconFolderZip"), // Vibrant Amber/Orange

            ".pdf" or ".doc" or ".docx" or ".ppt" or ".pptx" or ".xls" or ".xlsx" or ".txt" =>
                (new SolidColorBrush(Color.Parse("#EF4444")), "IconFile"), // Vibrant Red

            ".cs" or ".js" or ".ts" or ".json" or ".xml" or ".html" or ".css" or ".py" or ".cpp" or ".h" =>
                (new SolidColorBrush(Color.Parse("#10B981")), "IconCode"), // Vibrant Emerald

            _ => (new SolidColorBrush(Color.Parse("#3B82F6")), "IconFile") // Vibrant Electric Blue
        };
    }

    public static string FormatFileSize(long bytes)
    {
        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return $"{size:F2} {suffixes[suffixIndex]}";
    }
}
