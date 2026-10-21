using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinGomiExplorer.Models;
using WinGomiExplorer.Services;
using WinGomiExplorer.Views;

namespace WinGomiExplorer;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DiskScannerService _scannerService = new();
    private ExplorerTabViewModel? _activeTab;
    private ExplorerTabViewModel? _secondaryTab;
    private bool _showVisualDashboard = true;
    private bool _showDualPane;

    public ObservableCollection<ExplorerTabViewModel> Tabs { get; } = [];
    public ObservableCollection<QuickAccessItem> QuickAccessItems { get; } = [];

    public ExplorerTabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (_activeTab != value)
            {
                _activeTab = value;
                OnPropertyChanged();
            }
        }
    }

    public ExplorerTabViewModel? SecondaryTab
    {
        get => _secondaryTab;
        set
        {
            if (_secondaryTab != value)
            {
                _secondaryTab = value;
                OnPropertyChanged();
            }
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshDriveCards();

        var savedSession = SessionStateService.LoadSession();
        PopulateQuickAccess(savedSession?.CustomBookmarks);

        // 第2ペイン（デュアルペイン用）の初期化
        string secPath = savedSession?.SecondaryPanePath ?? @"C:\";
        if (!Directory.Exists(secPath)) secPath = @"C:\";
        SecondaryTab = new ExplorerTabViewModel(_scannerService, TabKind.FolderExplorer, secPath);

        if (savedSession is not null)
        {
            _showVisualDashboard = savedSession.ShowVisualDashboard;
            VisualDashboardPanel.Visibility = _showVisualDashboard ? Visibility.Visible : Visibility.Collapsed;

            if (savedSession.ShowDualPane)
            {
                await SetDualPaneVisibilityAsync(true);
            }
        }

        // 前回セッションのタブ復元、またはデフォルト初期タブの起動
        if (savedSession?.Tabs is { Count: > 0 })
        {
            foreach (var savedTab in savedSession.Tabs)
            {
                if (!Directory.Exists(savedTab.Path)) continue;
                var tab = new ExplorerTabViewModel(_scannerService, savedTab.Kind, savedTab.Path, savedTab.IsPinned);
                Tabs.Add(tab);
                if (savedTab.Kind == TabKind.FolderExplorer)
                {
                    await tab.NavigateToAsync(savedTab.Path, recordHistory: false);
                }
            }

            if (Tabs.Count > 0)
            {
                int activeIdx = Math.Clamp(savedSession.ActiveTabIndex, 0, Tabs.Count - 1);
                ActiveTab = Tabs[activeIdx];
                return;
            }
        }

        string initialPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(initialPath) || !Directory.Exists(initialPath))
        {
            initialPath = @"C:\";
        }

        await OpenNewExplorerTabAsync(initialPath);
    }

    private void RefreshDriveCards()
    {
        DrivesItemsControl.ItemsSource = DriveItem.GetAvailableDrives();
    }

    private void PopulateQuickAccess(List<SavedBookmarkInfo>? customBookmarks = null)
    {
        QuickAccessItems.Clear();

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localLowAppData = Path.Combine(userProfile, @"AppData\LocalLow");
        string downloads = Path.Combine(userProfile, "Downloads");
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string userTemp = Path.GetTempPath().TrimEnd('\\');

        // カスタムお気に入りピン留め
        if (customBookmarks is not null)
        {
            foreach (var bm in customBookmarks)
            {
                if (string.IsNullOrWhiteSpace(bm.TargetPath)) continue;
                QuickAccessItems.Add(new QuickAccessItem
                {
                    Icon = "⭐",
                    Title = bm.Title,
                    Subtitle = $"[お気に入り] {bm.TargetPath}",
                    TargetPath = bm.TargetPath,
                    IsJunkHotspot = false
                });
            }
        }

        var defaults = new List<QuickAccessItem>
        {
            new() { Icon = "💻", Title = "Cドライブ (C:\\)", Subtitle = "システムドライブ直下", TargetPath = @"C:\" },
            new() { Icon = "🏠", Title = "ユーザーホーム", Subtitle = userProfile, TargetPath = userProfile },
            new() { Icon = "📥", Title = "ダウンロード", Subtitle = "放置インストーラー・ZIP確認", TargetPath = downloads, IsJunkHotspot = true },
            new() { Icon = "🖥️", Title = "デスクトップ", Subtitle = desktop, TargetPath = desktop },
            new() { Icon = "📄", Title = "ドキュメント", Subtitle = documents, TargetPath = documents },
            new() { Icon = "👻", Title = "AppData\\Local (隠し)", Subtitle = "肥大化しやすいアプリデータ・キャッシュ", TargetPath = localAppData, IsJunkHotspot = true },
            new() { Icon = "👻", Title = "AppData\\Roaming (隠し)", Subtitle = "設定・共有アプリキャッシュ", TargetPath = roamingAppData, IsJunkHotspot = true },
            new() { Icon = "👻", Title = "AppData\\LocalLow (隠し)", Subtitle = localLowAppData, TargetPath = localLowAppData, IsJunkHotspot = true },
            new() { Icon = "🧹", Title = "ユーザー Temp (%TEMP%)", Subtitle = "一時ファイル・ゴミの宝庫", TargetPath = userTemp, IsJunkHotspot = true },
            new() { Icon = "🧹", Title = "Windows Temp", Subtitle = @"C:\Windows\Temp", TargetPath = @"C:\Windows\Temp", IsJunkHotspot = true },
            new() { Icon = "📋", Title = "CrashDumps (ダンプ)", Subtitle = Path.Combine(localAppData, "CrashDumps"), TargetPath = Path.Combine(localAppData, "CrashDumps"), IsJunkHotspot = true },
            new() { Icon = "⚡", Title = "DirectX ShaderCache", Subtitle = Path.Combine(localAppData, "D3DSCache"), TargetPath = Path.Combine(localAppData, "D3DSCache"), IsJunkHotspot = true },
            new() { Icon = "📦", Title = "WinUpdate キャッシュ", Subtitle = @"C:\Windows\SoftwareDistribution\Download", TargetPath = @"C:\Windows\SoftwareDistribution\Download", IsJunkHotspot = true }
        };

        foreach (var d in defaults)
        {
            QuickAccessItems.Add(d);
        }

        QuickAccessListBox.ItemsSource = QuickAccessItems;
    }

    // ==================== お気に入りブックマーク操作 ====================

    private void AddCurrentFolderBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;
        AddBookmarkForPath(ActiveTab.CurrentPath);
    }

    private void ContextBookmarkFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsDataGrid.SelectedItem is FileSystemItem selected)
        {
            string path = selected.IsDirectory ? selected.FullPath : selected.ParentPath;
            AddBookmarkForPath(path);
        }
    }

    private void AddBookmarkForPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        string norm = path.TrimEnd('\\');
        if (QuickAccessItems.Any(q => q.Icon == "⭐" && q.TargetPath.TrimEnd('\\').Equals(norm, StringComparison.OrdinalIgnoreCase)))
        {
            if (ActiveTab is not null)
                ActiveTab.StatusText = $"⭐ すでにお気に入りに登録されています: {path}";
            return;
        }

        string name = Path.GetFileName(norm);
        if (string.IsNullOrEmpty(name)) name = path;

        QuickAccessItems.Insert(0, new QuickAccessItem
        {
            Icon = "⭐",
            Title = name,
            Subtitle = $"[お気に入り] {path}",
            TargetPath = path
        });

        if (ActiveTab is not null)
            ActiveTab.StatusText = $"⭐ お気に入りにピン留めしました: {path}";
    }

    private void QuickAccessRemoveBookmark_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessListBox.SelectedItem is QuickAccessItem item)
        {
            if (item.Icon != "⭐")
            {
                MessageBox.Show("標準ショートカット項目は削除できません（⭐お気に入り追加した項目のみ削除可能です）。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            QuickAccessItems.Remove(item);
        }
    }

    // ==================== タブ操作 & ピン留め ====================

    public async Task OpenNewExplorerTabAsync(string path)
    {
        var tab = new ExplorerTabViewModel(_scannerService, TabKind.FolderExplorer, path);
        Tabs.Add(tab);
        ActiveTab = tab;
        await tab.NavigateToAsync(path, recordHistory: false);
    }

    public async Task OpenNewScannerTabAsync(string targetPath, bool autoStart = true)
    {
        var tab = new ExplorerTabViewModel(_scannerService, TabKind.LargeFileScanner, targetPath);
        Tabs.Add(tab);
        ActiveTab = tab;
        if (autoStart)
        {
            await tab.StartLargeScanAsync(targetPath);
        }
    }

    private void TogglePinActiveTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;
        ActiveTab.IsPinned = !ActiveTab.IsPinned;
        ActiveTab.StatusText = ActiveTab.IsPinned
            ? "📌 このタブをピン留め固定しました（終了時も優先保持されます）"
            : "📌 タブのピン留めを解除しました";
    }

    private async void NewExplorerTabButton_Click(object sender, RoutedEventArgs e)
    {
        string path = ActiveTab?.CurrentPath ?? @"C:\";
        if (!Directory.Exists(path)) path = @"C:\";
        await OpenNewExplorerTabAsync(path);
    }

    private async void NewScannerTabButton_Click(object sender, RoutedEventArgs e)
    {
        string path = ActiveTab?.CurrentPath ?? @"C:\";
        if (!Directory.Exists(path)) path = @"C:\";
        await OpenNewScannerTabAsync(path, autoStart: true);
    }

    private async void CloseTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ExplorerTabViewModel tab })
        {
            await CloseTabAsync(tab, forceClosePinned: false);
        }
    }

    private async Task CloseTabAsync(ExplorerTabViewModel tab, bool forceClosePinned)
    {
        if (tab.IsPinned && !forceClosePinned)
        {
            var res = MessageBox.Show(
                $"タブ「{tab.HeaderTitle}」はピン留め（📌）されています。閉じますか？",
                "ピン留めタブの確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
        }

        int index = Tabs.IndexOf(tab);
        tab.Dispose();
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            await OpenNewExplorerTabAsync(@"C:\");
        }
        else if (ActiveTab == tab || ActiveTab is null)
        {
            int nextIdx = Math.Clamp(index - 1, 0, Tabs.Count - 1);
            ActiveTab = Tabs[nextIdx];
        }
    }

    // ==================== ツリーマップ & 拡張子シェア & スマートクリーナー ====================

    private void ToggleVisualDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        _showVisualDashboard = !_showVisualDashboard;
        VisualDashboardPanel.Visibility = _showVisualDashboard ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Treemap_ItemClicked(FileSystemItem item)
    {
        ItemsDataGrid.SelectedItem = item;
        ItemsDataGrid.ScrollIntoView(item);
    }

    private async void Treemap_ItemDoubleClicked(FileSystemItem item)
    {
        if (ActiveTab is null) return;
        if (item.IsDirectory)
        {
            if (ActiveTab.IsExplorerTab)
                await ActiveTab.NavigateToAsync(item.FullPath);
            else
                await OpenNewExplorerTabAsync(item.FullPath);
        }
        else
        {
            OpenQuickPreview(item);
        }
    }

    private void ExtensionStatFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;
        if (sender is Button { Tag: string ext })
        {
            ActiveTab.SearchFilter = ActiveTab.SearchFilter == ext ? string.Empty : ext;
        }
    }

    private void ClearSearchFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is not null)
        {
            ActiveTab.SearchFilter = string.Empty;
        }
    }

    private void OpenSmartCleanerButton_Click(object sender, RoutedEventArgs e)
    {
        string currentPath = ActiveTab?.CurrentPath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var cleanerWindow = new SmartCleanerWindow(
            _scannerService,
            currentPath,
            OpenNewExplorerTabAsync,
            RefreshDriveCards)
        {
            Owner = this
        };
        cleanerWindow.ShowDialog();
    }

    // ==================== クイックプレビュー (Space) & 一括リネーム (F2) ====================

    private void QuickPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsDataGrid.SelectedItem is FileSystemItem selected)
        {
            OpenQuickPreview(selected);
        }
        else if (ActiveTab?.DisplayedItems.FirstOrDefault(i => i.IsSelected) is FileSystemItem checkedItem)
        {
            OpenQuickPreview(checkedItem);
        }
        else
        {
            MessageBox.Show("プレビューしたいファイルまたはフォルダを一覧から選択してください（または選択して Space キーを押してください）。", "クイックプレビュー", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenQuickPreview(FileSystemItem item)
    {
        var previewWin = new QuickPreviewWindow(item, _scannerService)
        {
            Owner = this
        };
        previewWin.ShowDialog();
    }

    private void BatchRenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;

        var targets = ActiveTab.DisplayedItems.Where(i => i.IsSelected).ToList();
        if (targets.Count == 0)
        {
            targets = ItemsDataGrid.SelectedItems.OfType<FileSystemItem>().ToList();
        }

        if (targets.Count == 0)
        {
            MessageBox.Show("一括リネームしたい項目をチェックボックスまたは行選択（Ctrl/Shift＋クリック）で選択してください。", "一括リネーム", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var renameWin = new BatchRenameWindow(targets)
        {
            Owner = this
        };

        if (renameWin.ShowDialog() == true)
        {
            _scannerService.InvalidatePathAndAncestors(ActiveTab.CurrentPath);
            ActiveTab.RefreshDisplayedItems();
            ActiveTab.StatusText = $"✅ {renameWin.RenamedCount:N0} 件の項目を一括リネームしました。";
        }
    }

    // ==================== 2画面分割 (デュアルペイン / Split View) ====================

    private async void ToggleDualPaneButton_Click(object sender, RoutedEventArgs e)
    {
        await SetDualPaneVisibilityAsync(!_showDualPane);
    }

    private async Task SetDualPaneVisibilityAsync(bool visible)
    {
        _showDualPane = visible;
        if (visible)
        {
            DualPaneTransferBar.Visibility = Visibility.Visible;
            DualPaneContainer.Visibility = Visibility.Visible;
            DualPaneColumn.Width = new GridLength(1, GridUnitType.Star);

            if (SecondaryTab is not null && SecondaryTab.DisplayedItems.Count == 0)
            {
                string startPath = ActiveTab?.CurrentPath ?? @"C:\";
                if (!Directory.Exists(startPath)) startPath = @"C:\";
                await SecondaryTab.NavigateToAsync(startPath, recordHistory: false);
            }
        }
        else
        {
            DualPaneTransferBar.Visibility = Visibility.Collapsed;
            DualPaneContainer.Visibility = Visibility.Collapsed;
            DualPaneColumn.Width = new GridLength(0);
        }
    }

    private async void SecondaryBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecondaryTab is not null) await SecondaryTab.GoBackAsync();
    }

    private async void SecondaryUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecondaryTab is not null) await SecondaryTab.GoUpAsync();
    }

    private async void SecondaryRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecondaryTab is not null) await SecondaryTab.NavigateToAsync(SecondaryTab.CurrentPath, false, true);
    }

    private async void SecondaryNavigateButton_Click(object sender, RoutedEventArgs e)
    {
        if (SecondaryTab is not null) await SecondaryTab.NavigateToAsync(SecondaryTab.AddressBarText);
    }

    private async void SecondaryAddressTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SecondaryTab is not null)
        {
            await SecondaryTab.NavigateToAsync(SecondaryTab.AddressBarText);
        }
    }

    private async void SecondaryDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SecondaryDataGrid.SelectedItem is FileSystemItem selected && SecondaryTab is not null)
        {
            if (selected.IsDirectory)
            {
                await SecondaryTab.NavigateToAsync(selected.FullPath);
            }
            else
            {
                OpenQuickPreview(selected);
            }
        }
    }

    private async void CopyToSecondaryPaneButton_Click(object sender, RoutedEventArgs e)
        => await TransferBetweenPanesAsync(fromPrimaryToSecondary: true, isMove: false);

    private async void MoveToSecondaryPaneButton_Click(object sender, RoutedEventArgs e)
        => await TransferBetweenPanesAsync(fromPrimaryToSecondary: true, isMove: true);

    private async void CopyToPrimaryPaneButton_Click(object sender, RoutedEventArgs e)
        => await TransferBetweenPanesAsync(fromPrimaryToSecondary: false, isMove: false);

    private async void MoveToPrimaryPaneButton_Click(object sender, RoutedEventArgs e)
        => await TransferBetweenPanesAsync(fromPrimaryToSecondary: false, isMove: true);

    private async Task TransferBetweenPanesAsync(bool fromPrimaryToSecondary, bool isMove)
    {
        if (ActiveTab is null || SecondaryTab is null) return;

        var srcTab = fromPrimaryToSecondary ? ActiveTab : SecondaryTab;
        var dstTab = fromPrimaryToSecondary ? SecondaryTab : ActiveTab;
        var srcGrid = fromPrimaryToSecondary ? ItemsDataGrid : SecondaryDataGrid;

        if (!Directory.Exists(dstTab.CurrentPath))
        {
            MessageBox.Show($"転送先フォルダが存在しません:\n{dstTab.CurrentPath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var items = srcTab.DisplayedItems.Where(i => i.IsSelected).ToList();
        if (items.Count == 0)
        {
            items = srcGrid.SelectedItems.OfType<FileSystemItem>().ToList();
        }

        if (items.Count == 0)
        {
            MessageBox.Show("転送したいファイルまたはフォルダを選択してください。", "デュアルペイン転送", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string actionName = isMove ? "移動" : "コピー";
        int done = 0;
        var errors = new List<string>();

        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                if (item.IsSystemProtected)
                {
                    errors.Add($"[システム保護] {item.Name}");
                    continue;
                }

                try
                {
                    string destPath = Path.Combine(dstTab.CurrentPath, item.Name);
                    if (string.Equals(item.FullPath, destPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (item.IsDirectory)
                    {
                        string srcPrefix = item.FullPath.TrimEnd('\\') + "\\";
                        if (destPath.StartsWith(srcPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.Add($"[無限再帰防止] {item.Name}: 親フォルダを自身のサブフォルダ内へ{actionName}することはできません。");
                            continue;
                        }

                        if (isMove)
                            Directory.Move(item.FullPath, destPath);
                        else
                            CopyDirectoryRecursive(item.FullPath, destPath);
                    }
                    else
                    {
                        if (isMove)
                            File.Move(item.FullPath, destPath, overwrite: true);
                        else
                            File.Copy(item.FullPath, destPath, overwrite: true);
                    }

                    _scannerService.InvalidatePathAndAncestors(item.FullPath);
                    _scannerService.InvalidatePathAndAncestors(destPath);
                    done++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.Name}: {ex.Message}");
                }
            }
        });

        if (srcTab.IsExplorerTab)
            await srcTab.NavigateToAsync(srcTab.CurrentPath, false, true);
        if (dstTab.IsExplorerTab)
            await dstTab.NavigateToAsync(dstTab.CurrentPath, false, true);

        ActiveTab.StatusText = $"✅ {done:N0} 件を {dstTab.CurrentPath} へ{actionName}しました。";
        if (errors.Count > 0)
        {
            MessageBox.Show($"{done} 件の{actionName}が完了しましたが、一部エラーがありました:\n" + string.Join("\n", errors.Take(5)), "転送結果", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }
        foreach (var sub in Directory.GetDirectories(sourceDir))
        {
            var di = new DirectoryInfo(sub);
            if ((di.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            string destSub = Path.Combine(targetDir, di.Name);
            CopyDirectoryRecursive(sub, destSub);
        }
    }

    // ==================== ショートカットキー制御 ====================

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.T)
            {
                e.Handled = true;
                string path = ActiveTab?.CurrentPath ?? @"C:\";
                if (!Directory.Exists(path)) path = @"C:\";
                await OpenNewExplorerTabAsync(path);
                return;
            }
            if (e.Key == Key.W && ActiveTab is not null)
            {
                e.Handled = true;
                await CloseTabAsync(ActiveTab, forceClosePinned: false);
                return;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && ActiveTab is { IsExplorerTab: true })
        {
            if (e.Key == Key.Left)
            {
                e.Handled = true;
                await ActiveTab.GoBackAsync();
                return;
            }
            if (e.Key == Key.Right)
            {
                e.Handled = true;
                await ActiveTab.GoForwardAsync();
                return;
            }
            if (e.Key == Key.Up)
            {
                e.Handled = true;
                await ActiveTab.GoUpAsync();
                return;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBox)
        {
            if (e.Key == Key.Space)
            {
                if (ItemsDataGrid.SelectedItem is FileSystemItem selected)
                {
                    e.Handled = true;
                    OpenQuickPreview(selected);
                    return;
                }
            }
            if (e.Key == Key.F2)
            {
                e.Handled = true;
                BatchRenameButton_Click(this, new RoutedEventArgs());
                return;
            }
            if (e.Key == Key.F5 && ActiveTab is not null)
            {
                e.Handled = true;
                if (ActiveTab.IsExplorerTab)
                    await ActiveTab.NavigateToAsync(ActiveTab.CurrentPath, recordHistory: false, forceRefreshSizes: true);
                else
                    await ActiveTab.StartLargeScanAsync();
                return;
            }
            if (e.Key == Key.Delete)
            {
                e.Handled = true;
                DeleteSelectedItemsButton_Click(this, new RoutedEventArgs());
            }
        }
    }

    // ==================== ドライブバー & クイックアクセス ====================

    private async void ScanCDriveButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenNewScannerTabAsync(@"C:\", autoStart: true);
    }

    private void RefreshDrivesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDriveCards();
    }

    private async void OpenDriveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string rootPath })
        {
            if (ActiveTab is { IsExplorerTab: true })
            {
                await ActiveTab.NavigateToAsync(rootPath);
            }
            else
            {
                await OpenNewExplorerTabAsync(rootPath);
            }
        }
    }

    private async void ScanDriveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string rootPath })
        {
            await OpenNewScannerTabAsync(rootPath, autoStart: true);
        }
    }

    private async void QuickAccessListBox_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (QuickAccessListBox.SelectedItem is QuickAccessItem item)
        {
            await NavigateOrWarnQuickAccessAsync(item, openInNewTab: false);
        }
    }

    private async void QuickAccessOpenCurrentTab_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessListBox.SelectedItem is QuickAccessItem item)
        {
            await NavigateOrWarnQuickAccessAsync(item, openInNewTab: false);
        }
    }

    private async void QuickAccessOpenNewTab_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessListBox.SelectedItem is QuickAccessItem item)
        {
            await NavigateOrWarnQuickAccessAsync(item, openInNewTab: true);
        }
    }

    private async void QuickAccessScan_Click(object sender, RoutedEventArgs e)
    {
        if (QuickAccessListBox.SelectedItem is QuickAccessItem item)
        {
            if (!Directory.Exists(item.TargetPath))
            {
                MessageBox.Show($"フォルダが存在しません:\n{item.TargetPath}", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            await OpenNewScannerTabAsync(item.TargetPath, autoStart: true);
        }
    }

    private async Task NavigateOrWarnQuickAccessAsync(QuickAccessItem item, bool openInNewTab)
    {
        if (!Directory.Exists(item.TargetPath))
        {
            MessageBox.Show(
                $"指定されたフォルダはこのPC上に存在しないか、まだ作成されていません:\n{item.TargetPath}",
                "フォルダ未検出",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (openInNewTab || ActiveTab is null || !ActiveTab.IsExplorerTab)
        {
            await OpenNewExplorerTabAsync(item.TargetPath);
        }
        else
        {
            await ActiveTab.NavigateToAsync(item.TargetPath);
        }
    }

    // ==================== ナビゲーション・ソート・スキャン操作 ====================

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is not null) await ActiveTab.GoBackAsync();
    }

    private async void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is not null) await ActiveTab.GoForwardAsync();
    }

    private async void UpButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is not null) await ActiveTab.GoUpAsync();
    }

    private async void RefreshExplorerButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is { IsExplorerTab: true })
        {
            await ActiveTab.NavigateToAsync(ActiveTab.CurrentPath, recordHistory: false, forceRefreshSizes: true);
        }
    }

    private async void NavigateAddressButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is { IsExplorerTab: true })
        {
            await ActiveTab.NavigateToAsync(ActiveTab.AddressBarText);
        }
    }

    private async void ExplorerAddressTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ActiveTab is { IsExplorerTab: true })
        {
            await ActiveTab.NavigateToAsync(ActiveTab.AddressBarText);
        }
    }

    private async void ScanCurrentFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is not null)
        {
            await OpenNewScannerTabAsync(ActiveTab.CurrentPath, autoStart: true);
        }
    }

    private async void StartScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is { IsScannerTab: true })
        {
            await ActiveTab.StartLargeScanAsync();
        }
    }

    private void StopScanButton_Click(object sender, RoutedEventArgs e)
    {
        ActiveTab?.StopCurrentWork();
    }

    private async void ScannerAddressTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ActiveTab is { IsScannerTab: true })
        {
            await ActiveTab.StartLargeScanAsync();
        }
    }

    private void SortBySizeDescButton_Click(object sender, RoutedEventArgs e)
        => ActiveTab?.ApplySort("Size", descending: true);

    private void SortByNameButton_Click(object sender, RoutedEventArgs e)
        => ActiveTab?.ApplySort("Name");

    private void SortByExtensionButton_Click(object sender, RoutedEventArgs e)
        => ActiveTab?.ApplySort("Extension");

    private void SortByDateButton_Click(object sender, RoutedEventArgs e)
        => ActiveTab?.ApplySort("ModifiedDate");

    private void ItemsDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (ActiveTab is null) return;

        string sortKey = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(sortKey)) return;

        ActiveTab.ApplySort(sortKey);
        e.Column.SortDirection = ActiveTab.SortDescending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
    }

    // ==================== DataGrid ダブルクリック & コンテキストメニュー ====================

    private async void ItemsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsDataGrid.SelectedItem is not FileSystemItem selected || ActiveTab is null)
            return;

        if (selected.IsDirectory)
        {
            if (ActiveTab.IsExplorerTab)
            {
                await ActiveTab.NavigateToAsync(selected.FullPath);
            }
            else
            {
                await OpenNewExplorerTabAsync(selected.FullPath);
            }
        }
        else
        {
            if (ActiveTab.IsScannerTab)
            {
                if (!string.IsNullOrEmpty(selected.ParentPath) && Directory.Exists(selected.ParentPath))
                {
                    await OpenNewExplorerTabAsync(selected.ParentPath);
                }
            }
            else
            {
                TryOpenFileWithDefaultApp(selected.FullPath);
            }
        }
    }

    private async void ContextOpen_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsDataGrid.SelectedItem is not FileSystemItem selected || ActiveTab is null)
            return;

        if (selected.IsDirectory)
        {
            if (ActiveTab.IsExplorerTab)
                await ActiveTab.NavigateToAsync(selected.FullPath);
            else
                await OpenNewExplorerTabAsync(selected.FullPath);
        }
        else
        {
            TryOpenFileWithDefaultApp(selected.FullPath);
        }
    }

    private async void ContextOpenInNewTab_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsDataGrid.SelectedItem is not FileSystemItem selected)
            return;

        string targetDir = selected.IsDirectory ? selected.FullPath : selected.ParentPath;
        if (Directory.Exists(targetDir))
        {
            await OpenNewExplorerTabAsync(targetDir);
        }
    }

    private async void ContextScanFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsDataGrid.SelectedItem is not FileSystemItem selected)
            return;

        string targetDir = selected.IsDirectory ? selected.FullPath : selected.ParentPath;
        if (Directory.Exists(targetDir))
        {
            await OpenNewScannerTabAsync(targetDir, autoStart: true);
        }
    }

    private void ContextCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsDataGrid.SelectedItem is FileSystemItem selected)
        {
            try
            {
                Clipboard.SetText(selected.FullPath);
                if (ActiveTab is not null)
                    ActiveTab.StatusText = $"📋 パスをコピーしました: {selected.FullPath}";
            }
            catch { }
        }
    }

    private void ContextRevealInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsDataGrid.SelectedItem is FileSystemItem selected)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{selected.FullPath}\"",
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    private async void ContextDeletePermanent_Click(object sender, RoutedEventArgs e)
    {
        var selectedRows = ItemsDataGrid.SelectedItems.OfType<FileSystemItem>().ToList();
        if (selectedRows.Count == 0) return;
        await ConfirmAndDeleteItemsAsync(selectedRows);
    }

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".msix", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".scr", ".com", ".reg", ".hta"
    };

    private static void TryOpenFileWithDefaultApp(string filePath)
    {
        try
        {
            string ext = Path.GetExtension(filePath);
            if (ExecutableExtensions.Contains(ext))
            {
                var confirm = MessageBox.Show(
                    $"このファイルは実行可能プログラムまたはスクリプト ({ext}) です。\n実行しますか？\n\n対象: {filePath}",
                    "実行の確認 (セキュリティガード)",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"ファイルを開けませんでした:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ==================== 一括選択 & 完全削除機能 ====================

    private void SelectAllJunkButton_Click(object sender, RoutedEventArgs e)
    {
        ActiveTab?.SelectAllJunkCandidates();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        ActiveTab?.ClearSelection();
    }

    private async void DeleteSelectedItemsButton_Click(object sender, RoutedEventArgs e)
    {
        if (ActiveTab is null) return;

        var checkedItems = ActiveTab.DisplayedItems.Where(i => i.IsSelected).ToList();
        if (checkedItems.Count == 0)
        {
            checkedItems = ItemsDataGrid.SelectedItems.OfType<FileSystemItem>().ToList();
        }

        if (checkedItems.Count == 0)
        {
            MessageBox.Show(
                "削除したいファイルまたはフォルダの左端にあるチェックボックスにチェックを入れるか、行を選択してください。",
                "項目が選択されていません",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await ConfirmAndDeleteItemsAsync(checkedItems);
    }

    private async Task ConfirmAndDeleteItemsAsync(IReadOnlyList<FileSystemItem> candidates)
    {
        var deletable = candidates.Where(i => !i.IsSystemProtected).ToList();
        if (deletable.Count == 0)
        {
            MessageBox.Show(
                "選択された項目はすべて「🛡️ システム保護対象」であるため、OS保護のため削除できません。",
                "システム保護ロック",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var confirmDialog = new DeleteConfirmWindow(deletable)
        {
            Owner = this
        };

        if (confirmDialog.ShowDialog() != true)
            return;

        if (ActiveTab is not null)
        {
            ActiveTab.IsBusy = true;
            ActiveTab.StatusText = $"🔥 完全削除を実行中 ({deletable.Count:N0} 件)...";
        }

        var report = await FileDeletionService.PermanentlyDeleteItemsAsync(deletable, _scannerService);

        foreach (var tab in Tabs)
        {
            tab.RemoveDeletedItemsFromView(report.CompletelyDeletedTopItems);
        }
        SecondaryTab?.RemoveDeletedItemsFromView(report.CompletelyDeletedTopItems);

        RefreshDriveCards();

        if (ActiveTab is not null)
        {
            ActiveTab.IsBusy = false;
            ActiveTab.StatusText = $"✅ 完全削除完了: {report.DeletedItemCount:N0} 項目 ({report.DeletedFileCount:N0} ファイル) を削除し、{FileSystemItem.FormatBytes(report.FreedBytes)} の空き容量を回復しました！";
        }

        string message =
            $"完全削除が完了しました。\n\n" +
            $"・削除成功項目数: {report.DeletedItemCount:N0} 件 (計 {report.DeletedFileCount:N0} ファイル)\n" +
            $"・解放された容量: {FileSystemItem.FormatBytes(report.FreedBytes)}";

        if (report.SkippedReasons.Count > 0)
        {
            string skippedPreview = string.Join("\n", report.SkippedReasons.Take(8));
            if (report.SkippedReasons.Count > 8)
                skippedPreview += $"\n...他 {report.SkippedReasons.Count - 8} 件";

            message += $"\n\n※一部使用中またはアクセス制限の項目:\n{skippedPreview}";
        }

        MessageBox.Show(message, "完全削除レポート", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ==================== セッション状態の自動保存 ====================

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            var state = new AppSessionState
            {
                ActiveTabIndex = ActiveTab is not null ? Math.Max(0, Tabs.IndexOf(ActiveTab)) : 0,
                ShowVisualDashboard = _showVisualDashboard,
                ShowDualPane = _showDualPane,
                SecondaryPanePath = SecondaryTab?.CurrentPath ?? @"C:\",
                Tabs = Tabs
                    .OrderByDescending(t => t.IsPinned)
                    .Select(t => new SavedTabInfo
                    {
                        Kind = t.Kind,
                        Path = t.CurrentPath,
                        IsPinned = t.IsPinned
                    })
                    .ToList(),
                CustomBookmarks = QuickAccessItems
                    .Where(q => q.Icon == "⭐")
                    .Select(q => new SavedBookmarkInfo
                    {
                        Title = q.Title,
                        TargetPath = q.TargetPath
                    })
                    .ToList()
            };

            SessionStateService.SaveSession(state);
        }
        catch { }

        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }
        SecondaryTab?.Dispose();

        base.OnClosed(e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
