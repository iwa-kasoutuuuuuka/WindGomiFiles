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

    public ObservableCollection<ExplorerTabViewModel> Tabs { get; } = [];

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

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshDriveCards();
        PopulateQuickAccess();

        // 初期タブとしてユーザープロファイル（または C:\）を開く
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

    private void PopulateQuickAccess()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string localLowAppData = Path.Combine(userProfile, @"AppData\LocalLow");
        string downloads = Path.Combine(userProfile, "Downloads");
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string userTemp = Path.GetTempPath().TrimEnd('\\');

        var list = new List<QuickAccessItem>
        {
            new() { Icon = "💻", Title = "Cドライブ (C:\\)", Subtitle = "システムドライブ直下", TargetPath = @"C:\" },
            new() { Icon = "🏠", Title = "ユーザーホーム", Subtitle = userProfile, TargetPath = userProfile },
            new() { Icon = "📥", Title = "ダウンロード", Subtitle = "放置インストーラー・ZIP確認", TargetPath = downloads, IsJunkHotspot = true },
            new() { Icon = "🖥️", Title = "デスクトップ", Subtitle = desktop, TargetPath = desktop },
            new() { Icon = "📄", Title = "ドキュメント", Subtitle = documents, TargetPath = documents },

            // 隠しフォルダ・ゴミ溜まりスポット直通
            new() { Icon = "👻", Title = "AppData\\Local (隠し)", Subtitle = "肥大化しやすいアプリデータ・キャッシュ", TargetPath = localAppData, IsJunkHotspot = true },
            new() { Icon = "👻", Title = "AppData\\Roaming (隠し)", Subtitle = "設定・共有アプリキャッシュ", TargetPath = roamingAppData, IsJunkHotspot = true },
            new() { Icon = "👻", Title = "AppData\\LocalLow (隠し)", Subtitle = localLowAppData, TargetPath = localLowAppData, IsJunkHotspot = true },
            new() { Icon = "🧹", Title = "ユーザー Temp (%TEMP%)", Subtitle = "一時ファイル・ゴミの宝庫", TargetPath = userTemp, IsJunkHotspot = true },
            new() { Icon = "🧹", Title = "Windows Temp", Subtitle = @"C:\Windows\Temp", TargetPath = @"C:\Windows\Temp", IsJunkHotspot = true },
            new() { Icon = "📋", Title = "CrashDumps (ダンプ)", Subtitle = Path.Combine(localAppData, "CrashDumps"), TargetPath = Path.Combine(localAppData, "CrashDumps"), IsJunkHotspot = true },
            new() { Icon = "⚡", Title = "DirectX ShaderCache", Subtitle = Path.Combine(localAppData, "D3DSCache"), TargetPath = Path.Combine(localAppData, "D3DSCache"), IsJunkHotspot = true },
            new() { Icon = "📦", Title = "WinUpdate キャッシュ", Subtitle = @"C:\Windows\SoftwareDistribution\Download", TargetPath = @"C:\Windows\SoftwareDistribution\Download", IsJunkHotspot = true }
        };

        QuickAccessListBox.ItemsSource = list;
    }

    // ==================== タブ操作 ====================

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
            await CloseTabAsync(tab);
        }
    }

    private async Task CloseTabAsync(ExplorerTabViewModel tab)
    {
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
                await CloseTabAsync(ActiveTab);
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
        else if (Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.Key == Key.F5 && ActiveTab is not null)
            {
                e.Handled = true;
                if (ActiveTab.IsExplorerTab)
                    await ActiveTab.NavigateToAsync(ActiveTab.CurrentPath, recordHistory: false, forceRefreshSizes: true);
                else
                    await ActiveTab.StartLargeScanAsync();
                return;
            }
            if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox)
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
                // スキャンタブでフォルダをダブルクリックした場合は新規タブでそのフォルダを開く
                await OpenNewExplorerTabAsync(selected.FullPath);
            }
        }
        else
        {
            if (ActiveTab.IsScannerTab)
            {
                // スキャンタブでファイルをダブルクリックした場合は、そのファイルがある親フォルダを新しいタブで開く
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

    protected override void OnClosed(EventArgs e)
    {
        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }
        base.OnClosed(e);
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
            // チェックボックスが未選択の場合は、DataGrid上でハイライト選択されている行を対象にする
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

        // 全タブから削除済み項目を反映
        foreach (var tab in Tabs)
        {
            tab.RemoveDeletedItemsFromView(report.CompletelyDeletedTopItems);
        }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
