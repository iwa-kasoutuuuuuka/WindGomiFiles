using System.IO;

namespace WinGomiExplorer.Models;

public class DriveItem
{
    public string Name { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public long UsedSpace => Math.Max(0, TotalSize - FreeSpace);
    public double UsedPercentage => TotalSize > 0 ? (double)UsedSpace / TotalSize * 100.0 : 0.0;
    public bool IsLowSpace => UsedPercentage >= 85.0 || (TotalSize > 0 && FreeSpace < 15L * 1024 * 1024 * 1024);

    public string DisplayTitle => string.IsNullOrWhiteSpace(VolumeLabel)
        ? $"{Name.TrimEnd('\\')}"
        : $"{VolumeLabel} ({Name.TrimEnd('\\')})";

    public string UsageSummary =>
        $"空き {FileSystemItem.FormatBytes(FreeSpace)} / 合計 {FileSystemItem.FormatBytes(TotalSize)} ({UsedPercentage:F1}% 使用)";

    public static List<DriveItem> GetAvailableDrives()
    {
        var result = new List<DriveItem>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                result.Add(new DriveItem
                {
                    Name = drive.Name,
                    RootPath = drive.RootDirectory.FullName,
                    VolumeLabel = drive.VolumeLabel,
                    TotalSize = drive.TotalSize,
                    FreeSpace = drive.AvailableFreeSpace
                });
            }
        }
        catch
        {
            // ドライブ列挙エラーは無視
        }
        return result;
    }
}

public class QuickAccessItem
{
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string Icon { get; set; } = "📁";
    public bool IsJunkHotspot { get; set; }
}
