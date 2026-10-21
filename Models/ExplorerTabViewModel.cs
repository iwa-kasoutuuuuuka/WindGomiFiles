using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using WinGomiExplorer.Services;

namespace WinGomiExplorer.Models;

public enum TabKind
{
    FolderExplorer,
    LargeFileScanner
}

public enum ScanFilterMode
{
    LargeFiles,
    LargeFolders,
    JunkFilesAndFolders,
    DuplicateFiles
}

public class SizeThresholdOption
{
    public string Label { get; init; } = string.Empty;
    public long Bytes { get; init; }
}

public class ExtensionStatItem
{
    public string Extension { get; init; } = string.Empty;
    public long TotalBytes { get; init; }
    public int Count { get; init; }
    public double Percentage { get; init; }
    public string DisplayText => $"{Extension}  {FileSystemItem.FormatBytes(TotalBytes)} ({Percentage:F1}%)";
}

public class ExplorerTabViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly DiskScannerService _scannerService;
    private CancellationTokenSource? _workCts;

    private string _headerTitle = "新しいタブ";
    private string _currentPath = @"C:\";
    private string _addressBarText = @"C:\";
    private string _searchFilter = string.Empty;
    private string _statusText = "準備完了";
    private bool _isBusy;
    private bool _isPinned;
    private bool _sortFoldersFirst;
    private string _currentSortBy = "Size";
    private bool _sortDescending = true;

    // スキャンタブ専用状態
    private long _minSizeThresholdBytes = 100L * 1024 * 1024; // デフォルト 100 MB
    private ScanFilterMode _scanFilterMode = ScanFilterMode.LargeFiles;
    private ScanResult? _lastScanResult;
    private List<FileSystemItem>? _cachedDuplicates;

    // 元データ保持
    private List<FileSystemItem> _rawDirectoryItems = [];

    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();

    public TabKind Kind { get; }
    public bool IsExplorerTab => Kind == TabKind.FolderExplorer;
    public bool IsScannerTab => Kind == TabKind.LargeFileScanner;

    public ObservableCollection<FileSystemItem> DisplayedItems { get; } = [];
    public ObservableCollection<ExtensionStatItem> ExtensionStats { get; } = [];
    public IEnumerable<FileSystemItem> TreemapItems => DisplayedItems.Take(48).ToList();

    public static List<SizeThresholdOption> ThresholdOptions { get; } =
    [
        new() { Label = "1 MB 以上", Bytes = 1L * 1024 * 1024 },
        new() { Label = "10 MB 以上", Bytes = 10L * 1024 * 1024 },
        new() { Label = "50 MB 以上", Bytes = 50L * 1024 * 1024 },
        new() { Label = "100 MB 以上 (推奨)", Bytes = 100L * 1024 * 1024 },
        new() { Label = "500 MB 以上", Bytes = 500L * 1024 * 1024 },
        new() { Label = "1 GB 以上", Bytes = 1024L * 1024 * 1024 }
    ];

    public ExplorerTabViewModel(DiskScannerService scannerService, TabKind kind, string initialPath, bool isPinned = false)
    {
        _scannerService = scannerService;
        Kind = kind;
        _currentPath = initialPath;
        _addressBarText = initialPath;
        _isPinned = isPinned;
        UpdateHeaderTitle();
    }

    public string HeaderTitle
    {
        get => _headerTitle;
        set { if (_headerTitle != value) { _headerTitle = value; OnPropertyChanged(); } }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned != value)
            {
                _isPinned = value;
                OnPropertyChanged();
                UpdateHeaderTitle();
            }
        }
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set
        {
            if (_currentPath != value)
            {
                _currentPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanGoUp));
                UpdateHeaderTitle();
            }
        }
    }

    public string AddressBarText
    {
        get => _addressBarText;
        set { if (_addressBarText != value) { _addressBarText = value; OnPropertyChanged(); } }
    }

    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (_searchFilter != value)
            {
                _searchFilter = value;
                OnPropertyChanged();
                RefreshDisplayedItems();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); } }
    }

    public bool SortFoldersFirst
    {
        get => _sortFoldersFirst;
        set
        {
            if (_sortFoldersFirst != value)
            {
                _sortFoldersFirst = value;
                OnPropertyChanged();
                RefreshDisplayedItems();
            }
        }
    }

    public string CurrentSortBy
    {
        get => _currentSortBy;
        set
        {
            if (_currentSortBy != value)
            {
                _currentSortBy = value;
                OnPropertyChanged();
            }
        }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (_sortDescending != value)
            {
                _sortDescending = value;
                OnPropertyChanged();
            }
        }
    }

    public long MinSizeThresholdBytes
    {
        get => _minSizeThresholdBytes;
        set
        {
            if (_minSizeThresholdBytes != value)
            {
                _minSizeThresholdBytes = value;
                ClearCachedDuplicates();
                OnPropertyChanged();
                if (IsScannerTab)
                {
                    _ = HandleScanFilterChangeAsync();
                }
            }
        }
    }

    private void ClearCachedDuplicates()
    {
        if (_cachedDuplicates is not null)
        {
            foreach (var dup in _cachedDuplicates)
                dup.PropertyChanged -= OnItemPropertyChanged;
            _cachedDuplicates = null;
        }
    }

    public ScanFilterMode ScanMode
    {
        get => _scanFilterMode;
        set
        {
            if (_scanFilterMode != value)
            {
                _scanFilterMode = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLargeFilesMode));
                OnPropertyChanged(nameof(IsLargeFoldersMode));
                OnPropertyChanged(nameof(IsJunkMode));
                OnPropertyChanged(nameof(IsDuplicatesMode));
                if (IsScannerTab)
                {
                    _ = HandleScanFilterChangeAsync();
                }
            }
        }
    }

    public bool IsLargeFilesMode
    {
        get => _scanFilterMode == ScanFilterMode.LargeFiles;
        set { if (value) ScanMode = ScanFilterMode.LargeFiles; }
    }

    public bool IsLargeFoldersMode
    {
        get => _scanFilterMode == ScanFilterMode.LargeFolders;
        set { if (value) ScanMode = ScanFilterMode.LargeFolders; }
    }

    public bool IsJunkMode
    {
        get => _scanFilterMode == ScanFilterMode.JunkFilesAndFolders;
        set { if (value) ScanMode = ScanFilterMode.JunkFilesAndFolders; }
    }

    public bool IsDuplicatesMode
    {
        get => _scanFilterMode == ScanFilterMode.DuplicateFiles;
        set { if (value) ScanMode = ScanFilterMode.DuplicateFiles; }
    }

    private async Task HandleScanFilterChangeAsync()
    {
        if (_scanFilterMode == ScanFilterMode.DuplicateFiles && _lastScanResult is not null && _cachedDuplicates is null)
        {
            IsBusy = true;
            var progress = new Progress<string>(msg => StatusText = msg);
            try
            {
                var dups = await DuplicateDetectorService.FindDuplicateFilesAsync(
                    _lastScanResult.LargeFiles,
                    _minSizeThresholdBytes,
                    progress,
                    CancellationToken.None);

                foreach (var item in dups)
                {
                    item.PropertyChanged += OnItemPropertyChanged;
                }
                _cachedDuplicates = dups;
            }
            finally
            {
                IsBusy = false;
            }
        }

        RefreshDisplayedItems();
    }

    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;
    public bool CanGoUp
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_currentPath)) return false;
            string? parent = Path.GetDirectoryName(_currentPath.TrimEnd('\\'));
            return !string.IsNullOrEmpty(parent);
        }
    }

    public int SelectedCount => DisplayedItems.Count(i => i.IsSelected);
    public long SelectedTotalBytes => CalculateNonOverlappingBytes(DisplayedItems.Where(i => i.IsSelected));

    public static long CalculateNonOverlappingBytes(IEnumerable<FileSystemItem> selectedItems)
    {
        var list = selectedItems.Where(i => i.Size > 0).ToList();
        if (list.Count <= 1) return list.Sum(i => i.Size);

        var selectedDirs = list
            .Where(i => i.IsDirectory)
            .Select(i => i.FullPath.TrimEnd('\\') + "\\")
            .OrderBy(p => p.Length)
            .ToList();

        long total = 0;
        foreach (var item in list)
        {
            string normalized = item.FullPath.TrimEnd('\\');
            bool isInsideSelectedDir = selectedDirs.Any(dir =>
                normalized.Length > dir.Length &&
                normalized.StartsWith(dir, StringComparison.OrdinalIgnoreCase));

            if (!isInsideSelectedDir)
            {
                total += item.Size;
            }
        }
        return total;
    }

    public string SelectionSummary
    {
        get
        {
            int count = SelectedCount;
            if (count == 0)
                return "チェックボックスで削除したい項目を選択できます (Spaceでプレビュー / F2で一括リネーム)";
            return $"選択中: {count:N0} 件  |  解放予定サイズ: {FileSystemItem.FormatBytes(SelectedTotalBytes)}";
        }
    }

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedTotalBytes));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    private void UpdateHeaderTitle()
    {
        string trimmed = _currentPath.TrimEnd('\\');
        string folderName = Path.GetFileName(trimmed);
        if (string.IsNullOrEmpty(folderName))
        {
            folderName = _currentPath;
        }

        string pinPrefix = _isPinned ? "📌 " : string.Empty;
        HeaderTitle = Kind == TabKind.FolderExplorer
            ? $"{pinPrefix}📁 {folderName}"
            : $"{pinPrefix}🔍 スキャン ({folderName})";
    }

    // ==================== フォルダ閲覧タブのナビゲーション ====================

    public async Task NavigateToAsync(string targetPath, bool recordHistory = true, bool forceRefreshSizes = false)
    {
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        targetPath = targetPath.Trim().Trim('"');
        if (!Directory.Exists(targetPath))
        {
            StatusText = $"⚠️ パスが見つかりません: {targetPath}";
            return;
        }

        CancelCurrentWork();

        if (recordHistory && !string.Equals(_currentPath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            _backStack.Push(_currentPath);
            _forwardStack.Clear();
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoForward));
        }

        CurrentPath = targetPath;
        AddressBarText = targetPath;

        var cts = new CancellationTokenSource();
        _workCts = cts;
        var ct = cts.Token;

        IsBusy = true;
        StatusText = $"読み込み中: {targetPath} ...";

        if (forceRefreshSizes)
        {
            _scannerService.InvalidatePathAndAncestors(targetPath);
        }

        List<FileSystemItem> items;
        try
        {
            items = await Task.Run(() => _scannerService.GetDirectoryItems(targetPath), ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested) return;

        foreach (var oldItem in _rawDirectoryItems)
        {
            oldItem.PropertyChanged -= OnItemPropertyChanged;
        }

        foreach (var item in items)
        {
            item.PropertyChanged += OnItemPropertyChanged;
        }

        _rawDirectoryItems = items;
        RefreshDisplayedItems();

        var foldersToCompute = items
            .Where(i => i.IsDirectory && !i.IsReparsePoint && i.IsCalculatingSize)
            .ToList();

        if (foldersToCompute.Count == 0)
        {
            IsBusy = false;
            UpdateExplorerSummaryStatus();
            return;
        }

        int totalFolders = foldersToCompute.Count;
        int completedFolders = 0;
        long lastUiSortTick = Environment.TickCount64;

        StatusText = $"📊 サブフォルダの容量を計算中... (0 / {totalFolders} フォルダ)";

        try
        {
            await Parallel.ForEachAsync(
                foldersToCompute,
                new ParallelOptions
                {
                    CancellationToken = ct,
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2)
                },
                async (folderItem, token) =>
                {
                    var (fSize, fCount) = await _scannerService.CalculateFolderSizeAsync(
                        folderItem.FullPath, forceRefreshSizes, token);

                    if (token.IsCancellationRequested) return;

                    int done = Interlocked.Increment(ref completedFolders);

                    var dispatcher = Application.Current?.Dispatcher;
                    if (dispatcher is null) return;

                    await dispatcher.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        folderItem.Size = fSize;
                        folderItem.FileCount = fCount;
                        folderItem.IsCalculatingSize = false;

                        StatusText = $"📊 サブフォルダの容量を計算中... ({done} / {totalFolders} フォルダ) - {folderItem.Name}";

                        long now = Environment.TickCount64;
                        if (now - Interlocked.Read(ref lastUiSortTick) >= 350 || done == totalFolders)
                        {
                            Interlocked.Exchange(ref lastUiSortTick, now);
                            if (_currentSortBy == "Size")
                            {
                                RefreshDisplayedItems();
                            }
                            else
                            {
                                RecalculateSizeRatios(DisplayedItems);
                                OnPropertyChanged(nameof(TreemapItems));
                            }
                        }
                    });
                });
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                IsBusy = false;
                RefreshDisplayedItems();
                UpdateExplorerSummaryStatus();
            }
        }
    }

    private void UpdateExplorerSummaryStatus()
    {
        int dirCount = _rawDirectoryItems.Count(i => i.IsDirectory);
        int fileCount = _rawDirectoryItems.Count(i => !i.IsDirectory);
        int hiddenCount = _rawDirectoryItems.Count(i => i.IsHidden || i.IsSystem);
        long totalBytes = _rawDirectoryItems.Where(i => i.Size > 0).Sum(i => i.Size);

        StatusText = $"合計: {FileSystemItem.FormatBytes(totalBytes)}  |  📁 {dirCount:N0} フォルダ, 📄 {fileCount:N0} ファイル (うち隠し/システム属性: {hiddenCount:N0} 件)";
    }

    public async Task GoBackAsync()
    {
        if (_backStack.Count == 0) return;
        string prev = _backStack.Pop();
        _forwardStack.Push(_currentPath);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        await NavigateToAsync(prev, recordHistory: false);
    }

    public async Task GoForwardAsync()
    {
        if (_forwardStack.Count == 0) return;
        string next = _forwardStack.Pop();
        _backStack.Push(_currentPath);
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
        await NavigateToAsync(next, recordHistory: false);
    }

    public async Task GoUpAsync()
    {
        if (!CanGoUp) return;
        string? parent = Path.GetDirectoryName(_currentPath.TrimEnd('\\'));
        if (!string.IsNullOrEmpty(parent))
        {
            await NavigateToAsync(parent, recordHistory: true);
        }
    }

    // ==================== 大容量・ゴミファイルスキャンタブの操作 ====================

    public async Task StartLargeScanAsync(string? targetRoot = null)
    {
        string scanPath = string.IsNullOrWhiteSpace(targetRoot) ? _addressBarText.Trim().Trim('"') : targetRoot.Trim().Trim('"');
        if (!Directory.Exists(scanPath))
        {
            StatusText = $"⚠️ スキャン対象のパスが存在しません: {scanPath}";
            return;
        }

        CancelCurrentWork();

        CurrentPath = scanPath;
        AddressBarText = scanPath;

        var cts = new CancellationTokenSource();
        _workCts = cts;
        var ct = cts.Token;

        IsBusy = true;
        ClearCachedDuplicates();
        DisplayedItems.Clear();
        NotifySelectionChanged();

        var progress = new Progress<ScanProgressReport>(report =>
        {
            if (ct.IsCancellationRequested) return;
            StatusText = $"🔍 スキャン中: {report.ScannedFiles:N0} ファイル / {report.ScannedDirectories:N0} フォルダ ({FileSystemItem.FormatBytes(report.TotalBytesScanned)})  |  検出: ファイル {report.LargeFilesFound:N0} 件 / フォルダ {report.LargeFoldersFound:N0} 件  |  {report.CurrentPath}";
        });

        try
        {
            long baseCollectThreshold = Math.Min(10L * 1024 * 1024, _minSizeThresholdBytes);
            var result = await _scannerService.ScanLargeItemsAsync(scanPath, baseCollectThreshold, progress, ct);

            if (_lastScanResult is not null)
            {
                foreach (var oldFile in _lastScanResult.LargeFiles)
                    oldFile.PropertyChanged -= OnItemPropertyChanged;
                foreach (var oldFolder in _lastScanResult.LargeFolders)
                    oldFolder.PropertyChanged -= OnItemPropertyChanged;
            }

            foreach (var item in result.LargeFiles)
                item.PropertyChanged += OnItemPropertyChanged;
            foreach (var item in result.LargeFolders)
                item.PropertyChanged += OnItemPropertyChanged;

            _lastScanResult = result;

            if (_scanFilterMode == ScanFilterMode.DuplicateFiles)
            {
                await HandleScanFilterChangeAsync();
            }
            else
            {
                RefreshDisplayedItems();
            }

            string stateLabel = result.WasCancelled ? "⏹ スキャン中断 (途中結果を表示中)" : "✅ スキャン完了";
            StatusText = $"{stateLabel} ({result.Elapsed.TotalSeconds:F1}秒)  |  走査: {result.TotalScannedFiles:N0} ファイル ({FileSystemItem.FormatBytes(result.TotalScannedBytes)})  |  表示中: {DisplayedItems.Count:N0} 件";
        }
        catch (Exception ex)
        {
            StatusText = $"⚠️ スキャンエラー: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void StopCurrentWork()
    {
        CancelCurrentWork();
        IsBusy = false;
        StatusText += " (停止しました)";
    }

    private void CancelCurrentWork()
    {
        if (_workCts is not null)
        {
            try { _workCts.Cancel(); } catch { }
            _workCts.Dispose();
            _workCts = null;
        }
    }

    // ==================== ソート・フィルタ・表示更新 ====================

    public void ApplySort(string sortByColumn, bool? descending = null)
    {
        if (descending.HasValue)
        {
            _sortDescending = descending.Value;
        }
        else if (_currentSortBy == sortByColumn)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortDescending = sortByColumn is "Size" or "ModifiedDate";
        }

        _currentSortBy = sortByColumn;
        OnPropertyChanged(nameof(CurrentSortBy));
        OnPropertyChanged(nameof(SortDescending));
        RefreshDisplayedItems();
    }

    public void RefreshDisplayedItems()
    {
        IEnumerable<FileSystemItem> source;

        if (Kind == TabKind.FolderExplorer)
        {
            source = _rawDirectoryItems;
        }
        else
        {
            if (_lastScanResult is null)
            {
                DisplayedItems.Clear();
                ExtensionStats.Clear();
                OnPropertyChanged(nameof(TreemapItems));
                NotifySelectionChanged();
                return;
            }

            long threshold = _minSizeThresholdBytes;
            source = _scanFilterMode switch
            {
                ScanFilterMode.LargeFiles => _lastScanResult.LargeFiles.Where(f => f.Size >= threshold),
                ScanFilterMode.LargeFolders => FilterRedundantParentFolders(_lastScanResult.LargeFolders.Where(d => d.Size >= threshold)),
                ScanFilterMode.JunkFilesAndFolders =>
                    _lastScanResult.LargeFiles
                        .Where(f => f.Size >= threshold && SystemProtectionService.IsJunkCandidate(f.Category))
                        .Concat(FilterRedundantParentFolders(
                            _lastScanResult.LargeFolders.Where(d => d.Size >= threshold && SystemProtectionService.IsJunkCandidate(d.Category)))),
                ScanFilterMode.DuplicateFiles => _cachedDuplicates ?? Enumerable.Empty<FileSystemItem>(),
                _ => _lastScanResult.LargeFiles.Where(f => f.Size >= threshold)
            };
        }

        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            string query = _searchFilter.Trim();
            source = source.Where(i =>
                i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.FullPath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.CategoryLabel.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Extension.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        IOrderedEnumerable<FileSystemItem> ordered;
        if (_scanFilterMode == ScanFilterMode.DuplicateFiles && Kind == TabKind.LargeFileScanner)
        {
            // 重複ファイルモードではペアが隣り合うようにサイズ降順＋グループ順を維持
            ordered = source.OrderByDescending(i => i.SortableSize).ThenBy(i => i.CategoryLabel);
        }
        else if (_sortFoldersFirst && Kind == TabKind.FolderExplorer)
        {
            ordered = source.OrderByDescending(i => i.IsDirectory);
            ordered = ApplySecondarySort(ordered);
        }
        else
        {
            ordered = ApplyPrimarySort(source);
        }

        var finalList = (Kind == TabKind.LargeFileScanner ? ordered.Take(1500) : ordered).ToList();

        RecalculateSizeRatios(finalList);
        UpdateExtensionStatistics(finalList);

        DisplayedItems.Clear();
        foreach (var item in finalList)
        {
            DisplayedItems.Add(item);
        }

        OnPropertyChanged(nameof(TreemapItems));
        NotifySelectionChanged();
    }

    private void UpdateExtensionStatistics(IReadOnlyList<FileSystemItem> list)
    {
        ExtensionStats.Clear();
        var sizedItems = list.Where(i => i.Size > 0).ToList();
        if (sizedItems.Count == 0) return;

        long totalBytes = sizedItems.Sum(i => i.Size);
        if (totalBytes <= 0) return;

        var groups = sizedItems
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Extension) ? "(なし)" : i.Extension.ToLowerInvariant())
            .Select(g => new ExtensionStatItem
            {
                Extension = g.Key,
                TotalBytes = g.Sum(x => x.Size),
                Count = g.Count(),
                Percentage = (double)g.Sum(x => x.Size) / totalBytes * 100.0
            })
            .OrderByDescending(x => x.TotalBytes)
            .Take(8)
            .ToList();

        foreach (var g in groups)
        {
            ExtensionStats.Add(g);
        }
    }

    private static IEnumerable<FileSystemItem> FilterRedundantParentFolders(IEnumerable<FileSystemItem> folders)
    {
        return folders.Take(1000);
    }

    private IOrderedEnumerable<FileSystemItem> ApplyPrimarySort(IEnumerable<FileSystemItem> items)
    {
        return (_currentSortBy, _sortDescending) switch
        {
            ("Size", true) => items.OrderByDescending(i => i.SortableSize).ThenBy(i => i.Name),
            ("Size", false) => items.OrderBy(i => i.SortableSize).ThenBy(i => i.Name),
            ("Name", true) => items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
            ("Name", false) => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            ("Extension", true) => items.OrderByDescending(i => i.Extension, StringComparer.OrdinalIgnoreCase).ThenByDescending(i => i.SortableSize),
            ("Extension", false) => items.OrderBy(i => i.Extension, StringComparer.OrdinalIgnoreCase).ThenByDescending(i => i.SortableSize),
            ("ModifiedDate", true) => items.OrderByDescending(i => i.ModifiedDate),
            ("ModifiedDate", false) => items.OrderBy(i => i.ModifiedDate),
            ("Category", true) => items.OrderByDescending(i => i.CategoryLabel).ThenByDescending(i => i.SortableSize),
            ("Category", false) => items.OrderBy(i => i.CategoryLabel).ThenByDescending(i => i.SortableSize),
            ("Attributes", true) => items.OrderByDescending(i => i.AttributesDisplay).ThenByDescending(i => i.SortableSize),
            ("Attributes", false) => items.OrderBy(i => i.AttributesDisplay).ThenByDescending(i => i.SortableSize),
            ("FullPath", true) => items.OrderByDescending(i => i.FullPath, StringComparer.OrdinalIgnoreCase),
            ("FullPath", false) => items.OrderBy(i => i.FullPath, StringComparer.OrdinalIgnoreCase),
            _ => items.OrderByDescending(i => i.SortableSize)
        };
    }

    private IOrderedEnumerable<FileSystemItem> ApplySecondarySort(IOrderedEnumerable<FileSystemItem> items)
    {
        return (_currentSortBy, _sortDescending) switch
        {
            ("Size", true) => items.ThenByDescending(i => i.SortableSize).ThenBy(i => i.Name),
            ("Size", false) => items.ThenBy(i => i.SortableSize).ThenBy(i => i.Name),
            ("Name", true) => items.ThenByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
            ("Name", false) => items.ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            ("Extension", true) => items.ThenByDescending(i => i.Extension, StringComparer.OrdinalIgnoreCase).ThenByDescending(i => i.SortableSize),
            ("Extension", false) => items.ThenBy(i => i.Extension, StringComparer.OrdinalIgnoreCase).ThenByDescending(i => i.SortableSize),
            ("ModifiedDate", true) => items.ThenByDescending(i => i.ModifiedDate),
            ("ModifiedDate", false) => items.ThenBy(i => i.ModifiedDate),
            ("Category", true) => items.ThenByDescending(i => i.CategoryLabel).ThenByDescending(i => i.SortableSize),
            ("Category", false) => items.ThenBy(i => i.CategoryLabel).ThenByDescending(i => i.SortableSize),
            ("Attributes", true) => items.ThenByDescending(i => i.AttributesDisplay).ThenByDescending(i => i.SortableSize),
            ("Attributes", false) => items.ThenBy(i => i.AttributesDisplay).ThenByDescending(i => i.SortableSize),
            ("FullPath", true) => items.ThenByDescending(i => i.FullPath, StringComparer.OrdinalIgnoreCase),
            ("FullPath", false) => items.ThenBy(i => i.FullPath, StringComparer.OrdinalIgnoreCase),
            _ => items.ThenByDescending(i => i.SortableSize)
        };
    }

    private static void RecalculateSizeRatios(IReadOnlyList<FileSystemItem> list)
    {
        long maxSize = 0;
        foreach (var item in list)
        {
            if (item.Size > maxSize)
                maxSize = item.Size;
        }

        foreach (var item in list)
        {
            item.SizeRatio = (maxSize > 0 && item.Size > 0)
                ? (double)item.Size / maxSize
                : 0.0;
        }
    }

    public void SelectAllJunkCandidates()
    {
        foreach (var item in DisplayedItems)
        {
            if (!item.IsSystemProtected && SystemProtectionService.IsJunkCandidate(item.Category))
            {
                item.IsSelected = true;
            }
        }
        NotifySelectionChanged();
    }

    public void ClearSelection()
    {
        foreach (var item in DisplayedItems)
        {
            item.IsSelected = false;
        }
        NotifySelectionChanged();
    }

    public void RemoveDeletedItemsFromView(IEnumerable<FileSystemItem> deletedItems)
    {
        var deletedList = deletedItems.ToList();
        if (deletedList.Count == 0) return;

        var deletedExactSet = new HashSet<string>(
            deletedList.Select(i => i.FullPath.TrimEnd('\\')),
            StringComparer.OrdinalIgnoreCase);

        var deletedDirPrefixes = deletedList
            .Where(i => i.IsDirectory)
            .Select(i => i.FullPath.TrimEnd('\\') + "\\")
            .ToList();

        bool ShouldRemove(FileSystemItem candidate)
        {
            string norm = candidate.FullPath.TrimEnd('\\');
            if (deletedExactSet.Contains(norm))
                return true;

            foreach (var prefix in deletedDirPrefixes)
            {
                if (norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        foreach (var item in _rawDirectoryItems.Where(ShouldRemove))
            item.PropertyChanged -= OnItemPropertyChanged;

        _rawDirectoryItems.RemoveAll(ShouldRemove);

        if (_lastScanResult is not null)
        {
            foreach (var item in _lastScanResult.LargeFiles.Where(ShouldRemove))
                item.PropertyChanged -= OnItemPropertyChanged;
            foreach (var item in _lastScanResult.LargeFolders.Where(ShouldRemove))
                item.PropertyChanged -= OnItemPropertyChanged;

            _lastScanResult.LargeFiles.RemoveAll(ShouldRemove);
            _lastScanResult.LargeFolders.RemoveAll(ShouldRemove);
        }

        if (_cachedDuplicates is not null)
        {
            foreach (var item in _cachedDuplicates.Where(ShouldRemove))
                item.PropertyChanged -= OnItemPropertyChanged;
            _cachedDuplicates.RemoveAll(ShouldRemove);
        }

        RefreshDisplayedItems();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileSystemItem.IsSelected))
        {
            NotifySelectionChanged();
        }
    }

    public void Dispose()
    {
        CancelCurrentWork();
        foreach (var item in _rawDirectoryItems)
            item.PropertyChanged -= OnItemPropertyChanged;
        if (_lastScanResult is not null)
        {
            foreach (var f in _lastScanResult.LargeFiles)
                f.PropertyChanged -= OnItemPropertyChanged;
            foreach (var d in _lastScanResult.LargeFolders)
                d.PropertyChanged -= OnItemPropertyChanged;
        }
        if (_cachedDuplicates is not null)
        {
            foreach (var dup in _cachedDuplicates)
                dup.PropertyChanged -= OnItemPropertyChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
