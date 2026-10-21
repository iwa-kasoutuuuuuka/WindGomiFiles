using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using WinGomiExplorer.Models;
using WinGomiExplorer.Services;

namespace WinGomiExplorer.Views;

public partial class SmartCleanerWindow : Window
{
    private readonly DiskScannerService _scanner;
    private readonly Func<string, Task> _openTabCallback;
    private readonly Action _refreshDrivesCallback;
    private List<FileSystemItem> _currentItems = [];

    public SmartCleanerWindow(
        DiskScannerService scanner,
        string initialTargetFolder,
        Func<string, Task> openTabCallback,
        Action refreshDrivesCallback)
    {
        InitializeComponent();
        _scanner = scanner;
        _openTabCallback = openTabCallback;
        _refreshDrivesCallback = refreshDrivesCallback;

        TargetFolderTextBox.Text = string.IsNullOrWhiteSpace(initialTargetFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : initialTargetFolder;

        Loaded += async (_, _) => await RunDevCacheScanAsync();
    }

    private async void ScanDevCachesButton_Click(object sender, RoutedEventArgs e)
    {
        await RunDevCacheScanAsync();
    }

    private async Task RunDevCacheScanAsync()
    {
        ResultsGrid.Visibility = Visibility.Visible;
        DiagReportTextBox.Visibility = Visibility.Collapsed;
        StatusText.Text = "🔍 開発ツールキャッシュ (npm/pip/cargo/NuGet/Gradle/Docker) & システムTempを計測中...";

        var items = await DuplicateDetectorService.ScanDeveloperAndSystemCachesAsync(_scanner, CancellationToken.None);
        foreach (var item in items)
        {
            // キャッシュクリーン画面ではチェックボックスを選択可能にする（実行時に器を保護しつつ中身を全削除）
            item.IsSystemProtected = false;
            if (item.Category is ItemCategory.TempOrJunk or ItemCategory.Cache or ItemCategory.LogOrDump)
            {
                item.IsSelected = true;
            }
        }

        _currentItems = items;
        ResultsGrid.ItemsSource = _currentItems;
        long total = items.Sum(i => Math.Max(0, i.Size));
        StatusText.Text = $"✅ 検出完了: {items.Count:N0} 箇所のキャッシュ・一時領域 (合計 {FileSystemItem.FormatBytes(total)})";
    }

    private async void ScanEmptyFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        string target = TargetFolderTextBox.Text.Trim().Trim('"');
        if (!Directory.Exists(target))
        {
            MessageBox.Show($"対象フォルダが存在しません:\n{target}", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // ドライブルート直下全体の空フォルダスキャンは時間がかかるため確認
        if (target.Length <= 3)
        {
            var res = MessageBox.Show(
                $"ドライブルート ({target}) 全体の空フォルダ・0バイトファイルを検索しますか？\n（特定のフォルダを調べる場合は右上の対象フォルダ欄に入力してください）",
                "空フォルダ検出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (res != MessageBoxResult.Yes) return;
        }

        ResultsGrid.Visibility = Visibility.Visible;
        DiagReportTextBox.Visibility = Visibility.Collapsed;
        StatusText.Text = $"🔍 空フォルダ・0バイトファイルを検索中: {target} ...";

        var items = await DuplicateDetectorService.FindEmptyFoldersAndZeroByteFilesAsync(target, CancellationToken.None);
        _currentItems = items;
        ResultsGrid.ItemsSource = _currentItems;
        StatusText.Text = $"✅ 検出完了: 空フォルダ・0バイトファイル 計 {items.Count:N0} 件";
    }

    private async void RunWindowsDiagButton_Click(object sender, RoutedEventArgs e)
    {
        ResultsGrid.Visibility = Visibility.Collapsed;
        DiagReportTextBox.Visibility = Visibility.Visible;
        StatusText.Text = "🛡️ Windows ストレージ & コンポーネント残骸レポートを生成中...";

        string reportText = await Task.Run(async () =>
        {
            var sb = new StringBuilder();
            sb.AppendLine("==========================================================================");
            sb.AppendLine(" 🛡️ Windows システム領域・アップデート残骸・仮想ディスク診断レポート");
            sb.AppendLine("==========================================================================");
            sb.AppendLine();

            var checkPaths = new (string Path, string Description)[]
            {
                (@"C:\Windows.old", "旧Windowsアップグレード残骸 (Windows.old)"),
                (@"C:\$WINDOWS.~BT", "Windows大型アップデート一時領域 ($WINDOWS.~BT)"),
                (@"C:\Windows\SoftwareDistribution\Download", "Windows Update ダウンロードキャッシュ"),
                (@"C:\Windows\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache", "配信の最適化 (Delivery Optimization) キャッシュ"),
                (@"C:\ProgramData\Microsoft\Windows\WER\ReportArchive", "Windows Error Reporting (エラー報告アーカイブ)"),
                (@"C:\ProgramData\Microsoft\Windows\WER\ReportQueue", "Windows Error Reporting (エラー報告キュー)"),
                (@"C:\ProgramData\Package Cache", "インストーラー Package Cache"),
                (@"C:\Windows\WinSxS", "Windows コンポーネントストア (WinSxS ※直接削除不可・DISM管理)")
            };

            foreach (var (path, desc) in checkPaths)
            {
                if (Directory.Exists(path))
                {
                    var (sz, cnt) = await _scanner.CalculateFolderSizeAsync(path, false, CancellationToken.None);
                    sb.AppendLine($"[検出] {desc}");
                    sb.AppendLine($"  ・パス   : {path}");
                    sb.AppendLine($"  ・サイズ : {FileSystemItem.FormatBytes(sz)} ({cnt:N0} ファイル)");
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine($"[なし] {desc} ({path})");
                }
            }

            sb.AppendLine("--------------------------------------------------------------------------");
            sb.AppendLine("💡 【WinSxS や休止状態ファイル (hiberfil.sys) を安全に削減する管理者コマンド】");
            sb.AppendLine("  1. 古い更新プログラムの完全クリーンアップ (管理者PowerShell/cmd):");
            sb.AppendLine("     dism.exe /Online /Cleanup-Image /StartComponentCleanup /ResetBase");
            sb.AppendLine();
            sb.AppendLine("  2. 休止状態 (hiberfil.sys 数GB〜数十GB) を使わない場合の無効化コマンド:");
            sb.AppendLine("     powercfg.exe /hibernate off");
            sb.AppendLine();
            sb.AppendLine("  3. WSL2 / Docker 仮想ディスク (ext4.vhdx) の未使用領域圧縮:");
            sb.AppendLine("     wsl --shutdown");
            sb.AppendLine("     (その後 Docker Desktop または Optimize-VHD / diskpart compact vdisk を実行)");
            sb.AppendLine("==========================================================================");

            return sb.ToString();
        });

        DiagReportTextBox.Text = reportText;
        StatusText.Text = "✅ Windows システム診断レポート生成完了";
    }

    private async void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsGrid.SelectedItem is FileSystemItem selected)
        {
            string path = selected.IsDirectory ? selected.FullPath : selected.ParentPath;
            if (Directory.Exists(path))
            {
                await _openTabCallback(path);
            }
        }
    }

    private async void OpenSelectedInTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsGrid.SelectedItem is FileSystemItem selected)
        {
            string path = selected.IsDirectory ? selected.FullPath : selected.ParentPath;
            if (Directory.Exists(path))
            {
                await _openTabCallback(path);
            }
        }
    }

    private async void CleanSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _currentItems.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("クリーンアップしたい項目にチェックを入れてください。", "スマートクリーン", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 保護コンテナ（%TEMP% や C:\Windows\Temp 等）が選択されている場合は、器自体は残して「その直下の全アイテム」を削除対象に展開する
        var expandedTargets = new List<FileSystemItem>();
        foreach (var item in selected)
        {
            if (item.IsDirectory && SystemProtectionService.IsSystemProtectedPath(item.FullPath, isDirectory: true))
            {
                var children = _scanner.GetDirectoryItems(item.FullPath)
                    .Where(c => !c.IsSystemProtected)
                    .ToList();
                expandedTargets.AddRange(children);
            }
            else
            {
                expandedTargets.Add(item);
            }
        }

        if (expandedTargets.Count == 0)
        {
            MessageBox.Show("削除可能なファイルが見つかりませんでした（すでに空か、保護されています）。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = new DeleteConfirmWindow(expandedTargets) { Owner = this };
        if (confirm.ShowDialog() != true) return;

        StatusText.Text = "🔥 クリーンアップを実行中...";
        var report = await FileDeletionService.PermanentlyDeleteItemsAsync(expandedTargets, _scanner);
        _refreshDrivesCallback();

        StatusText.Text = $"✅ クリーンアップ完了: {report.DeletedFileCount:N0} ファイルを削除し、{FileSystemItem.FormatBytes(report.FreedBytes)} を解放しました！";
        MessageBox.Show(
            $"クリーンアップが完了しました！\n\n・削除ファイル数: {report.DeletedFileCount:N0} 件\n・解放された容量: {FileSystemItem.FormatBytes(report.FreedBytes)}",
            "スマートクリーン完了",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        await RunDevCacheScanAsync();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
