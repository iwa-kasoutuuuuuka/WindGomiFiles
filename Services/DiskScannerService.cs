using System.Collections.Concurrent;
using System.IO;
using System.IO.Enumeration;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Services;

public class ScanProgressReport
{
    public long ScannedFiles { get; init; }
    public long ScannedDirectories { get; init; }
    public long TotalBytesScanned { get; init; }
    public string CurrentPath { get; init; } = string.Empty;
    public int LargeFilesFound { get; init; }
    public int LargeFoldersFound { get; init; }
}

public class ScanResult
{
    public List<FileSystemItem> LargeFiles { get; init; } = [];
    public List<FileSystemItem> LargeFolders { get; init; } = [];
    public long TotalScannedFiles { get; init; }
    public long TotalScannedDirectories { get; init; }
    public long TotalScannedBytes { get; init; }
    public TimeSpan Elapsed { get; init; }
    public bool WasCancelled { get; init; }
}

public class DiskScannerService
{
    // フォルダサイズキャッシュ (Key: 正規化パス, Value: (Size, FileCount))
    private readonly ConcurrentDictionary<string, (long Size, int FileCount)> _folderSizeCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly EnumerationOptions AllItemsSingleLevelOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = 0, // 隠しファイル・システムファイルもすべて取得
        ReturnSpecialDirectories = false
    };

    public void ClearCache() => _folderSizeCache.Clear();

    public void InvalidatePathAndAncestors(string path)
    {
        string? current = path;
        while (!string.IsNullOrEmpty(current))
        {
            _folderSizeCache.TryRemove(NormalizePath(current), out _);
            current = Path.GetDirectoryName(current);
        }
    }

    public bool TryGetCachedFolderSize(string folderPath, out long size, out int fileCount)
    {
        if (_folderSizeCache.TryGetValue(NormalizePath(folderPath), out var entry))
        {
            size = entry.Size;
            fileCount = entry.FileCount;
            return true;
        }
        size = -1;
        fileCount = 0;
        return false;
    }

    /// <summary>
    /// 指定したディレクトリ直下のすべてのファイル・フォルダ（隠しファイル・システムファイル・拡張子すべて含む）を取得します。
    /// </summary>
    public List<FileSystemItem> GetDirectoryItems(string directoryPath)
    {
        var items = new List<FileSystemItem>();
        if (!Directory.Exists(directoryPath))
            return items;

        try
        {
            var enumerable = new FileSystemEnumerable<FileSystemItem>(
                directoryPath,
                (ref FileSystemEntry entry) =>
                {
                    string name = entry.FileName.ToString();
                    string fullPath = entry.ToFullPath();
                    bool isDir = entry.IsDirectory;
                    string ext = isDir ? "<DIR>" : Path.GetExtension(name);
                    if (!isDir && string.IsNullOrEmpty(ext))
                    {
                        ext = "(なし)";
                    }

                    var (category, label, isProtected) = SystemProtectionService.ClassifyItem(
                        fullPath, name, isDir ? string.Empty : Path.GetExtension(name), isDir);

                    long size = isDir ? -1 : entry.Length;

                    return new FileSystemItem
                    {
                        Name = name,
                        FullPath = fullPath,
                        Extension = ext,
                        IsDirectory = isDir,
                        Size = size,
                        ModifiedDate = entry.LastWriteTimeUtc.LocalDateTime,
                        Attributes = entry.Attributes,
                        Category = category,
                        CategoryLabel = label,
                        IsSystemProtected = isProtected
                    };
                },
                AllItemsSingleLevelOptions);

            foreach (var item in enumerable)
            {
                if (item.IsDirectory && !item.IsReparsePoint)
                {
                    if (TryGetCachedFolderSize(item.FullPath, out long cachedSize, out int cachedCount))
                    {
                        item.Size = cachedSize;
                        item.FileCount = cachedCount;
                    }
                    else
                    {
                        item.IsCalculatingSize = true;
                        item.Size = -2;
                    }
                }
                items.Add(item);
            }
        }
        catch
        {
            // アクセス拒否等の場合は取得できた分のみ返す
        }

        return items;
    }

    /// <summary>
    /// 単一フォルダの合計サイズを非同期・ボトムアップで高速計算し、サブフォルダも含めてキャッシュします。
    /// </summary>
    public Task<(long Size, int FileCount)> CalculateFolderSizeAsync(
        string folderPath,
        bool forceRefresh,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            string normPath = NormalizePath(folderPath);
            if (!forceRefresh && _folderSizeCache.TryGetValue(normPath, out var cached))
            {
                return cached;
            }

            return ComputeSubtreeSizeSync(folderPath, forceRefresh, ct);
        }, ct);
    }

    private (long Size, int FileCount) ComputeSubtreeSizeSync(
        string dirPath,
        bool forceRefresh,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string normPath = NormalizePath(dirPath);
        if (!forceRefresh && _folderSizeCache.TryGetValue(normPath, out var cached))
        {
            return cached;
        }

        long totalSize = 0;
        int totalFiles = 0;
        var subDirs = new List<string>();

        try
        {
            var enumerable = new FileSystemEnumerable<(bool IsDir, bool IsReparse, long Length, string? SubDirPath)>(
                dirPath,
                (ref FileSystemEntry entry) =>
                {
                    bool isDir = entry.IsDirectory;
                    bool isReparse = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                    if (isDir)
                    {
                        return (true, isReparse, 0L, isReparse ? null : entry.ToFullPath());
                    }
                    return (false, isReparse, entry.Length, null);
                },
                AllItemsSingleLevelOptions);

            foreach (var entry in enumerable)
            {
                if (ct.IsCancellationRequested) break;
                if (entry.IsDir)
                {
                    if (!entry.IsReparse && entry.SubDirPath is not null)
                    {
                        subDirs.Add(entry.SubDirPath);
                    }
                }
                else
                {
                    totalSize += entry.Length;
                    totalFiles++;
                }
            }
        }
        catch
        {
            // アクセス権のないディレクトリはスキップ
            return (0, 0);
        }

        ct.ThrowIfCancellationRequested();

        // サブディレクトリが多い場合は並列計算、少ない場合は順次計算でオーバーヘッドを抑える
        if (subDirs.Count > 1)
        {
            long subSizeSum = 0;
            int subFilesSum = 0;

            Parallel.ForEach(
                subDirs,
                new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount)
                },
                subDir =>
                {
                    var (sSize, sCount) = ComputeSubtreeSizeSync(subDir, forceRefresh, ct);
                    Interlocked.Add(ref subSizeSum, sSize);
                    Interlocked.Add(ref subFilesSum, sCount);
                });

            totalSize += subSizeSum;
            totalFiles += subFilesSum;
        }
        else if (subDirs.Count == 1)
        {
            var (sSize, sCount) = ComputeSubtreeSizeSync(subDirs[0], forceRefresh, ct);
            totalSize += sSize;
            totalFiles += sCount;
        }

        if (!ct.IsCancellationRequested)
        {
            _folderSizeCache[normPath] = (totalSize, totalFiles);
        }

        return (totalSize, totalFiles);
    }

    /// <summary>
    /// 指定ドライブ・フォルダ配下をフルスキャンし、大容量ファイルおよび大容量フォルダのランキングデータを抽出します。
    /// 収集時の基準値 (baseThresholdBytes, デフォルト 10MB) 以上のものをすべて収集するため、
    /// スキャン完了後は UI 側で 10MB / 50MB / 100MB / 500MB / 1GB のフィルタを瞬時に切り替えられます。
    /// </summary>
    public async Task<ScanResult> ScanLargeItemsAsync(
        string rootPath,
        long baseThresholdBytes,
        IProgress<ScanProgressReport>? progress,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var largeFiles = new ConcurrentBag<FileSystemItem>();
        var largeFolders = new ConcurrentBag<FileSystemItem>();

        long scannedFiles = 0;
        long scannedDirs = 0;
        long totalBytes = 0;
        string currentPath = rootPath;
        long lastReportTick = Environment.TickCount64;

        void MaybeReportProgress(bool force = false)
        {
            if (progress is null) return;
            long now = Environment.TickCount64;
            if (!force && now - Interlocked.Read(ref lastReportTick) < 120) return;
            Interlocked.Exchange(ref lastReportTick, now);

            progress.Report(new ScanProgressReport
            {
                ScannedFiles = Interlocked.Read(ref scannedFiles),
                ScannedDirectories = Interlocked.Read(ref scannedDirs),
                TotalBytesScanned = Interlocked.Read(ref totalBytes),
                CurrentPath = Volatile.Read(ref currentPath),
                LargeFilesFound = largeFiles.Count,
                LargeFoldersFound = largeFolders.Count
            });
        }

        (long Size, int FileCount) ScanDirectoryRecursive(string dirPath, int depth)
        {
            if (ct.IsCancellationRequested) return (0, 0);

            Interlocked.Increment(ref scannedDirs);
            if (depth <= 3)
            {
                Volatile.Write(ref currentPath, dirPath);
            }
            MaybeReportProgress();

            long dirTotalSize = 0;
            int dirTotalFiles = 0;
            var subDirs = new List<(string FullPath, string Name, FileAttributes Attr, DateTime LastWrite)>();

            try
            {
                var enumerable = new FileSystemEnumerable<(
                    bool IsDir,
                    bool IsReparse,
                    string Name,
                    string FullPath,
                    long Length,
                    FileAttributes Attr,
                    DateTime LastWrite)>(
                    dirPath,
                    (ref FileSystemEntry entry) =>
                    {
                        bool isDir = entry.IsDirectory;
                        bool isReparse = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                        string name = entry.FileName.ToString();
                        string full = entry.ToFullPath();
                        long len = isDir ? 0L : entry.Length;
                        return (isDir, isReparse, name, full, len, entry.Attributes, entry.LastWriteTimeUtc.LocalDateTime);
                    },
                    AllItemsSingleLevelOptions);

                foreach (var item in enumerable)
                {
                    if (ct.IsCancellationRequested) break;

                    if (item.IsDir)
                    {
                        if (!item.IsReparse)
                        {
                            subDirs.Add((item.FullPath, item.Name, item.Attr, item.LastWrite));
                        }
                    }
                    else
                    {
                        dirTotalSize += item.Length;
                        dirTotalFiles++;
                        Interlocked.Increment(ref scannedFiles);
                        Interlocked.Add(ref totalBytes, item.Length);

                        if (item.Length >= baseThresholdBytes)
                        {
                            string rawExt = Path.GetExtension(item.Name);
                            string extDisplay = string.IsNullOrEmpty(rawExt) ? "(なし)" : rawExt;
                            var (cat, label, isProt) = SystemProtectionService.ClassifyItem(
                                item.FullPath, item.Name, rawExt, isDirectory: false);

                            largeFiles.Add(new FileSystemItem
                            {
                                Name = item.Name,
                                FullPath = item.FullPath,
                                Extension = extDisplay,
                                IsDirectory = false,
                                Size = item.Length,
                                ModifiedDate = item.LastWrite,
                                Attributes = item.Attr,
                                Category = cat,
                                CategoryLabel = label,
                                IsSystemProtected = isProt
                            });
                        }
                    }
                }
            }
            catch
            {
                return (0, 0);
            }

            if (ct.IsCancellationRequested) return (dirTotalSize, dirTotalFiles);

            // 上位階層は並列走査し、深い階層はシリアル走査にすることでスレッドプール飽和を防ぐ
            if (depth <= 2 && subDirs.Count > 1)
            {
                long subSizeSum = 0;
                int subFilesSum = 0;

                Parallel.ForEach(
                    subDirs,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount)
                    },
                    sub =>
                    {
                        if (ct.IsCancellationRequested) return;
                        var (sSize, sCount) = ScanDirectoryRecursive(sub.FullPath, depth + 1);
                        Interlocked.Add(ref subSizeSum, sSize);
                        Interlocked.Add(ref subFilesSum, sCount);

                        RecordLargeFolderIfQualified(sub.FullPath, sub.Name, sub.Attr, sub.LastWrite, sSize, sCount);
                    });

                dirTotalSize += subSizeSum;
                dirTotalFiles += subFilesSum;
            }
            else
            {
                foreach (var sub in subDirs)
                {
                    if (ct.IsCancellationRequested) break;
                    var (sSize, sCount) = ScanDirectoryRecursive(sub.FullPath, depth + 1);
                    dirTotalSize += sSize;
                    dirTotalFiles += sCount;

                    RecordLargeFolderIfQualified(sub.FullPath, sub.Name, sub.Attr, sub.LastWrite, sSize, sCount);
                }
            }

            if (!ct.IsCancellationRequested)
            {
                _folderSizeCache[NormalizePath(dirPath)] = (dirTotalSize, dirTotalFiles);
            }

            return (dirTotalSize, dirTotalFiles);
        }

        void RecordLargeFolderIfQualified(
            string fullPath,
            string name,
            FileAttributes attr,
            DateTime lastWrite,
            long folderSize,
            int fileCount)
        {
            if (folderSize < baseThresholdBytes) return;

            var (cat, label, isProt) = SystemProtectionService.ClassifyItem(
                fullPath, name, string.Empty, isDirectory: true);

            largeFolders.Add(new FileSystemItem
            {
                Name = name,
                FullPath = fullPath,
                Extension = "<DIR>",
                IsDirectory = true,
                Size = folderSize,
                FileCount = fileCount,
                ModifiedDate = lastWrite,
                Attributes = attr,
                Category = cat,
                CategoryLabel = label,
                IsSystemProtected = isProt
            });
        }

        DiagnosticsService.LogInfo("DiskScanner", $"Starting large scan on '{rootPath}' (BaseThreshold={baseThresholdBytes} bytes)");
        await Task.Run(() => ScanDirectoryRecursive(rootPath, 0), CancellationToken.None);

        sw.Stop();
        MaybeReportProgress(force: true);
        DiagnosticsService.LogInfo("DiskScanner", $"Scan finished for '{rootPath}' in {sw.Elapsed.TotalSeconds:F2}s (Files={scannedFiles}, Dirs={scannedDirs}, Bytes={totalBytes}, Cancelled={ct.IsCancellationRequested})");

        var sortedFiles = largeFiles
            .OrderByDescending(f => f.Size)
            .ToList();

        var sortedFolders = largeFolders
            .OrderByDescending(d => d.Size)
            .ToList();

        return new ScanResult
        {
            LargeFiles = sortedFiles,
            LargeFolders = sortedFolders,
            TotalScannedFiles = scannedFiles,
            TotalScannedDirectories = scannedDirs,
            TotalScannedBytes = totalBytes,
            Elapsed = sw.Elapsed,
            WasCancelled = ct.IsCancellationRequested
        };
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\');
        }
        catch
        {
            return path.TrimEnd('\\');
        }
    }
}
