using System.IO;
using System.IO.Enumeration;
using System.Security.Cryptography;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Services;

public class DuplicateGroup
{
    public string HashKey { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public List<FileSystemItem> Files { get; init; } = [];
    public long WastedBytes => Files.Count > 1 ? FileSize * (Files.Count - 1) : 0;
}

public static class DuplicateDetectorService
{
    /// <summary>
    /// ファイルリストの中から、サイズが同一かつ内容（高速フィンガープリント＋SHA256）が一致する重複ファイルを検出します。
    /// </summary>
    public static Task<List<FileSystemItem>> FindDuplicateFilesAsync(
        IEnumerable<FileSystemItem> candidateFiles,
        long minSizeBytes,
        IProgress<string>? statusProgress,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            // 1. まず同一サイズのファイルグループ（2件以上）に絞り込む
            var sizeGroups = candidateFiles
                .Where(f => !f.IsDirectory && f.Size >= minSizeBytes && !f.IsSystemProtected)
                .GroupBy(f => f.Size)
                .Where(g => g.Count() >= 2)
                .OrderByDescending(g => g.Key * (g.Count() - 1))
                .ToList();

            var resultItems = new List<FileSystemItem>();
            int totalGroups = sizeGroups.Count;
            int processedGroups = 0;
            int groupNumber = 1;

            foreach (var sizeGroup in sizeGroups)
            {
                ct.ThrowIfCancellationRequested();
                processedGroups++;

                if (processedGroups % 5 == 1 || processedGroups == totalGroups)
                {
                    statusProgress?.Report($"🧬 重複ハッシュ照合中... ({processedGroups:N0} / {totalGroups:N0} サイズ一致グループ)");
                }

                // 2. 高速フィンガープリント（先頭64KB + 中間64KB + 末尾64KB の SHA256）で比較
                var hashBuckets = new Dictionary<string, List<FileSystemItem>>(StringComparer.Ordinal);
                foreach (var file in sizeGroup)
                {
                    if (ct.IsCancellationRequested) break;
                    string? fastHash = ComputeFastContentHash(file.FullPath, file.Size);
                    if (fastHash is null) continue;

                    if (!hashBuckets.TryGetValue(fastHash, out var list))
                    {
                        list = [];
                        hashBuckets[fastHash] = list;
                    }
                    list.Add(file);
                }

                foreach (var kv in hashBuckets)
                {
                    if (kv.Value.Count < 2) continue;

                    // 更新日時が最も古い（またはパスが短い）ものを [原本] とし、残りを [重複コピー] としてラベル付け
                    var ordered = kv.Value
                        .OrderBy(f => f.ModifiedDate)
                        .ThenBy(f => f.FullPath.Length)
                        .ToList();

                    for (int i = 0; i < ordered.Count; i++)
                    {
                        var src = ordered[i];
                        bool isCopy = i > 0;
                        var clone = new FileSystemItem
                        {
                            Name = src.Name,
                            FullPath = src.FullPath,
                            Extension = src.Extension,
                            IsDirectory = false,
                            Size = src.Size,
                            ModifiedDate = src.ModifiedDate,
                            Attributes = src.Attributes,
                            Category = isCopy ? ItemCategory.TempOrJunk : ItemCategory.Normal,
                            CategoryLabel = isCopy
                                ? $"🧬 重複 #{groupNumber} (削除推奨コピー)"
                                : $"📌 重複 #{groupNumber} (残す原本)",
                            IsSystemProtected = src.IsSystemProtected,
                            IsSelected = isCopy // 重複コピー側をデフォルトで選択可能に
                        };
                        resultItems.Add(clone);
                    }

                    groupNumber++;
                }
            }

            return resultItems;
        }, ct);
    }

    /// <summary>
    /// 巨大ファイルでも数ミリ秒で比較できるよう、4MB以下は全量SHA256、
    /// 4MB超は先頭64KB・中央64KB・末尾64KBのサンプルSHA256を計算します。
    /// </summary>
    public static string? ComputeFastContentHash(string filePath, long fileSize)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();

            const int chunkSize = 64 * 1024; // 64 KB
            if (fileSize <= chunkSize * 4)
            {
                byte[] hash = sha.ComputeHash(fs);
                return Convert.ToHexString(hash);
            }

            byte[] buffer = new byte[chunkSize * 3];
            // 先頭 64KB
            int read1 = fs.Read(buffer, 0, chunkSize);
            // 中央 64KB
            fs.Seek(fileSize / 2, SeekOrigin.Begin);
            int read2 = fs.Read(buffer, chunkSize, chunkSize);
            // 末尾 64KB
            fs.Seek(Math.Max(0, fileSize - chunkSize), SeekOrigin.Begin);
            int read3 = fs.Read(buffer, chunkSize * 2, chunkSize);

            byte[] finalHash = sha.ComputeHash(buffer, 0, read1 + read2 + read3);
            return $"{fileSize:X}_{Convert.ToHexString(finalHash)}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 指定ルート配下の「空フォルダ」および「0バイトの空ファイル」を高速検出します。
    /// </summary>
    public static Task<List<FileSystemItem>> FindEmptyFoldersAndZeroByteFilesAsync(
        string rootPath,
        CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var results = new List<FileSystemItem>();
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            bool ScanEmptyRecursive(string dirPath, int depth)
            {
                if (ct.IsCancellationRequested || depth > 24) return false;
                if (SystemProtectionService.IsSystemProtectedPath(dirPath, isDirectory: true))
                    return false;

                bool hasNonEmptyChild = false;
                int entryCount = 0;

                try
                {
                    var enumerable = new FileSystemEnumerable<(bool IsDir, bool IsReparse, string Name, string FullPath, long Length, FileAttributes Attr, DateTime LastWrite)>(
                        dirPath,
                        (ref FileSystemEntry entry) => (
                            entry.IsDirectory,
                            (entry.Attributes & FileAttributes.ReparsePoint) != 0,
                            entry.FileName.ToString(),
                            entry.ToFullPath(),
                            entry.IsDirectory ? 0L : entry.Length,
                            entry.Attributes,
                            entry.LastWriteTimeUtc.LocalDateTime
                        ),
                        options);

                    foreach (var item in enumerable)
                    {
                        if (ct.IsCancellationRequested) return true;
                        entryCount++;

                        if (item.IsDir)
                        {
                            if (item.IsReparse)
                            {
                                hasNonEmptyChild = true;
                            }
                            else
                            {
                                bool subIsEmpty = ScanEmptyRecursive(item.FullPath, depth + 1);
                                if (!subIsEmpty)
                                {
                                    hasNonEmptyChild = true;
                                }
                            }
                        }
                        else
                        {
                            if (item.Length == 0 && !SystemProtectionService.IsSystemProtectedPath(item.FullPath, isDirectory: false))
                            {
                                results.Add(new FileSystemItem
                                {
                                    Name = item.Name,
                                    FullPath = item.FullPath,
                                    Extension = Path.GetExtension(item.Name),
                                    IsDirectory = false,
                                    Size = 0,
                                    ModifiedDate = item.LastWrite,
                                    Attributes = item.Attr,
                                    Category = ItemCategory.TempOrJunk,
                                    CategoryLabel = "🧹 0バイト空ファイル",
                                    IsSystemProtected = false,
                                    IsSelected = true
                                });
                            }
                            else
                            {
                                hasNonEmptyChild = true;
                            }
                        }
                    }
                }
                catch
                {
                    return false;
                }

                if (entryCount == 0 && depth > 0)
                {
                    var di = new DirectoryInfo(dirPath);
                    results.Add(new FileSystemItem
                    {
                        Name = di.Name,
                        FullPath = dirPath,
                        Extension = "<EMPTY DIR>",
                        IsDirectory = true,
                        Size = 0,
                        ModifiedDate = di.LastWriteTime,
                        Attributes = di.Attributes,
                        Category = ItemCategory.TempOrJunk,
                        CategoryLabel = "📂 空フォルダ",
                        IsSystemProtected = false,
                        IsSelected = true
                    });
                    return true;
                }

                return !hasNonEmptyChild && entryCount == 0;
            }

            ScanEmptyRecursive(rootPath, 0);
            return results;
        }, ct);
    }

    /// <summary>
    /// PC内の代表的な開発ツールキャッシュ・肥大化仮想ディスク (Docker/WSL vhdx, NuGet, npm, pip, Cargo, Gradle 等) をスキャンします。
    /// </summary>
    public static Task<List<FileSystemItem>> ScanDeveloperAndSystemCachesAsync(
        DiskScannerService scannerService,
        CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            var results = new List<FileSystemItem>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var knownTargets = new List<(string Path, string Label, ItemCategory Category)>
            {
                (Path.Combine(userProfile, @".nuget\packages"), "🔧 NuGet パッケージキャッシュ", ItemCategory.DevCache),
                (Path.Combine(localAppData, @"NuGet\v3-cache"), "🔧 NuGet HTTP キャッシュ", ItemCategory.DevCache),
                (Path.Combine(localAppData, "npm-cache"), "🔧 npm キャッシュ", ItemCategory.DevCache),
                (Path.Combine(roamingAppData, "npm-cache"), "🔧 npm キャッシュ (Roaming)", ItemCategory.DevCache),
                (Path.Combine(localAppData, @"pip\cache"), "🔧 Python pip キャッシュ", ItemCategory.DevCache),
                (Path.Combine(localAppData, "uv", "cache"), "🔧 Python uv キャッシュ", ItemCategory.DevCache),
                (Path.Combine(userProfile, @".cargo\registry"), "🔧 Rust Cargo レジストリキャッシュ", ItemCategory.DevCache),
                (Path.Combine(userProfile, @".cargo\git"), "🔧 Rust Cargo Git キャッシュ", ItemCategory.DevCache),
                (Path.Combine(userProfile, @".gradle\caches"), "🔧 Gradle ビルドキャッシュ", ItemCategory.DevCache),
                (Path.Combine(userProfile, @".m2\repository"), "🔧 Maven リポジトリキャッシュ", ItemCategory.DevCache),
                (Path.Combine(localAppData, @"Docker\wsl"), "🐳 Docker WSL 仮想ディスク領域", ItemCategory.DevCache),
                (Path.Combine(localAppData, @"Packages"), "📦 UWP / WSL パッケージ領域", ItemCategory.Cache),
                (Path.Combine(localAppData, @"Temp"), "🧹 ユーザー Temp (%TEMP%)", ItemCategory.TempOrJunk),
                (Path.Combine(localAppData, @"CrashDumps"), "📋 アプリ クラッシュダンプ", ItemCategory.LogOrDump),
                (Path.Combine(localAppData, @"D3DSCache"), "⚡ DirectX シェーダーキャッシュ", ItemCategory.Cache),
                (Path.Combine(localAppData, @"Microsoft\Windows\Explorer"), "⚡ エクスプローラー サムネイルキャッシュ", ItemCategory.Cache),
                (Path.Combine(localAppData, @"Microsoft\Windows\INetCache"), "⚡ IE/Edge 一時インターネットキャッシュ", ItemCategory.Cache),
                (Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Cache"), "⚡ Chrome ブラウザキャッシュ", ItemCategory.Cache),
                (Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Cache"), "⚡ Edge ブラウザキャッシュ", ItemCategory.Cache),
                (@"C:\Windows\Temp", "🧹 Windows システム Temp", ItemCategory.TempOrJunk),
                (@"C:\Windows\SoftwareDistribution\Download", "📦 Windows Update ダウンロード残骸", ItemCategory.TempOrJunk),
                (@"C:\Windows\Prefetch", "⚡ Windows Prefetch キャッシュ", ItemCategory.Cache),
                (@"C:\Windows\Minidump", "📋 Windows ブルースクリーン Minidump", ItemCategory.LogOrDump),
                (@"C:\Windows\LiveKernelReports", "📋 Windows カーネルレポートダンプ", ItemCategory.LogOrDump)
            };

            foreach (var target in knownTargets)
            {
                if (ct.IsCancellationRequested) break;
                if (!Directory.Exists(target.Path)) continue;

                try
                {
                    var (size, count) = await scannerService.CalculateFolderSizeAsync(target.Path, forceRefresh: false, ct);
                    if (size <= 0) continue;

                    var di = new DirectoryInfo(target.Path);
                    bool isProtectedContainer = SystemProtectionService.IsSystemProtectedPath(target.Path, isDirectory: true);

                    results.Add(new FileSystemItem
                    {
                        Name = $"{di.Name} ({target.Label})",
                        FullPath = target.Path,
                        Extension = "<DIR>",
                        IsDirectory = true,
                        Size = size,
                        FileCount = count,
                        ModifiedDate = di.LastWriteTime,
                        Attributes = di.Attributes,
                        Category = target.Category,
                        CategoryLabel = target.Label,
                        // 器自体が保護されている場合（%TEMP% 等）は、ダブルクリックで中身を開いて掃除できるよう表示
                        IsSystemProtected = isProtectedContainer
                    });
                }
                catch
                {
                    // アクセス不可はスキップ
                }
            }

            return results.OrderByDescending(i => i.Size).ToList();
        }, ct);
    }
}
