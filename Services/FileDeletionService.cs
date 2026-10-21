using System.IO;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Services;

public class DeletionReport
{
    public int DeletedItemCount { get; set; }
    public int DeletedFileCount { get; set; }
    public long FreedBytes { get; set; }
    public List<string> SkippedReasons { get; } = [];
    public List<FileSystemItem> CompletelyDeletedTopItems { get; } = [];
}

public static class FileDeletionService
{
    private const FileAttributes RestrictiveAttributes =
        FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;

    /// <summary>
    /// 指定されたアイテム群を直接「完全削除」します。
    /// システム保護領域は自動的にブロックし、フォルダ内に一部ロック中ファイルがあっても
    /// 削除可能なファイルをすべて削除して最大限ディスク容量を解放します。
    /// </summary>
    public static Task<DeletionReport> PermanentlyDeleteItemsAsync(
        IEnumerable<FileSystemItem> items,
        DiskScannerService scannerService)
    {
        return Task.Run(() =>
        {
            var report = new DeletionReport();

            // パスの浅い順（親ディレクトリ優先）に並べて重複削除試行を防ぐ
            var orderedItems = items
                .OrderBy(i => i.FullPath.Length)
                .ToList();

            foreach (var item in orderedItems)
            {
                if (item.IsSystemProtected || SystemProtectionService.IsSystemProtectedPath(item.FullPath, item.IsDirectory))
                {
                    DiagnosticsService.LogWarning("FileDeletion", $"Blocked deletion of system protected path: {item.FullPath}");
                    report.SkippedReasons.Add($"[システム保護] {item.FullPath}");
                    continue;
                }

                try
                {
                    if (item.IsDirectory)
                    {
                        if (!Directory.Exists(item.FullPath))
                        {
                            report.CompletelyDeletedTopItems.Add(item);
                            continue;
                        }

                        var (freed, filesDeleted, allCleared, errorMsg) = DeleteDirectoryResilient(item.FullPath);
                        report.FreedBytes += freed;
                        report.DeletedFileCount += filesDeleted;

                        if (allCleared)
                        {
                            report.DeletedItemCount++;
                            report.CompletelyDeletedTopItems.Add(item);
                        }
                        else
                        {
                            if (freed > 0)
                            {
                                report.DeletedItemCount++;
                                report.SkippedReasons.Add($"[一部使用中のため残存 ({FileSystemItem.FormatBytes(freed)} 解放済)] {item.Name}: {errorMsg}");
                            }
                            else
                            {
                                report.SkippedReasons.Add($"[削除失敗] {item.Name}: {errorMsg}");
                            }
                        }

                        scannerService.InvalidatePathAndAncestors(item.FullPath);
                    }
                    else
                    {
                        if (!File.Exists(item.FullPath))
                        {
                            report.CompletelyDeletedTopItems.Add(item);
                            continue;
                        }

                        long fileLen = 0;
                        try
                        {
                            var fi = new FileInfo(item.FullPath);
                            fileLen = fi.Length;
                            ClearRestrictiveAttributes(fi);
                        }
                        catch
                        {
                            // 属性変更失敗時も削除は試行
                        }

                        File.Delete(item.FullPath);
                        report.FreedBytes += fileLen > 0 ? fileLen : Math.Max(0, item.Size);
                        report.DeletedFileCount++;
                        report.DeletedItemCount++;
                        report.CompletelyDeletedTopItems.Add(item);

                        scannerService.InvalidatePathAndAncestors(item.FullPath);
                    }
                }
                catch (Exception ex)
                {
                    DiagnosticsService.LogError("FileDeletion", $"Failed to delete '{item.FullPath}'", ex);
                    report.SkippedReasons.Add($"[エラー] {item.Name}: {ex.Message}");
                }
            }

            DiagnosticsService.LogInfo("FileDeletion", $"Deletion finished: Items={report.DeletedItemCount}, Files={report.DeletedFileCount}, FreedBytes={report.FreedBytes}, Skipped={report.SkippedReasons.Count}");
            return report;
        });
    }

    private static (long FreedBytes, int DeletedFiles, bool CompletelyRemoved, string? LastError) DeleteDirectoryResilient(string dirPath)
    {
        long freedBytes = 0;
        int deletedFiles = 0;
        string? lastError = null;
        bool hasRemainingEntries = false;

        if (SystemProtectionService.IsSystemProtectedPath(dirPath, isDirectory: true))
        {
            return (0, 0, false, "システム保護対象のためスキップされました");
        }

        try
        {
            var dirInfo = new DirectoryInfo(dirPath);
            if (!dirInfo.Exists)
                return (0, 0, true, null);

            // ジャンクション・シンボリックリンクの場合はリンク先の中身を消さずリンク自体のみ削除
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                ClearRestrictiveAttributes(dirInfo);
                dirInfo.Delete(false);
                return (0, 0, true, null);
            }

            // 1. 直下のファイルを個別に削除
            FileInfo[] files = [];
            try
            {
                files = dirInfo.GetFiles("*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            foreach (var file in files)
            {
                if (SystemProtectionService.IsSystemProtectedPath(file.FullName, isDirectory: false))
                {
                    hasRemainingEntries = true;
                    lastError = $"保護ファイル ({file.Name}) をスキップ";
                    continue;
                }

                try
                {
                    long len = file.Length;
                    ClearRestrictiveAttributes(file);
                    file.Delete();
                    freedBytes += len;
                    deletedFiles++;
                }
                catch (Exception ex)
                {
                    hasRemainingEntries = true;
                    lastError = ex.Message;
                }
            }

            // 2. サブディレクトリを再帰的に削除
            DirectoryInfo[] subDirs = [];
            try
            {
                subDirs = dirInfo.GetDirectories("*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            foreach (var sub in subDirs)
            {
                var (subFreed, subDeleted, subRemoved, subErr) = DeleteDirectoryResilient(sub.FullName);
                freedBytes += subFreed;
                deletedFiles += subDeleted;
                if (!subRemoved)
                {
                    hasRemainingEntries = true;
                    if (!string.IsNullOrEmpty(subErr))
                        lastError = subErr;
                }
            }

            // 3. 中身がすべて消えたらディレクトリ自体を削除
            if (!hasRemainingEntries)
            {
                ClearRestrictiveAttributes(dirInfo);
                dirInfo.Delete(false);
                return (freedBytes, deletedFiles, true, null);
            }
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            hasRemainingEntries = true;
        }

        return (freedBytes, deletedFiles, !hasRemainingEntries, lastError);
    }

    private static void ClearRestrictiveAttributes(FileSystemInfo fsi)
    {
        var attrs = fsi.Attributes;
        if ((attrs & RestrictiveAttributes) != 0)
        {
            var newAttrs = attrs & ~RestrictiveAttributes;
            if (newAttrs == 0)
            {
                newAttrs = FileAttributes.Normal;
            }
            fsi.Attributes = newAttrs;
        }
    }
}
