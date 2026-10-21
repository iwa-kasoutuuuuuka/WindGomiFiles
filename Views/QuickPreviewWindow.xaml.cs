using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WinGomiExplorer.Models;
using WinGomiExplorer.Services;

namespace WinGomiExplorer.Views;

public partial class QuickPreviewWindow : Window
{
    private readonly FileSystemItem _item;
    private readonly DiskScannerService _scanner;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tiff", ".webp"
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".md", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".conf",
        ".csv", ".tsv", ".cs", ".xaml", ".csproj", ".sln", ".py", ".js", ".ts", ".tsx",
        ".jsx", ".html", ".htm", ".css", ".scss", ".sql", ".sh", ".bat", ".cmd", ".ps1",
        ".reg", ".gitignore", ".env", ".toml", ".rs", ".go", ".c", ".cpp", ".h"
    };

    public QuickPreviewWindow(FileSystemItem item, DiskScannerService scanner)
    {
        InitializeComponent();
        _item = item;
        _scanner = scanner;

        TitleText.Text = $"{item.IconSymbol} {item.Name}";
        PathText.Text = item.FullPath;
        SizeBadgeText.Text = item.Size >= 0 ? $"{item.SizeDisplay} ({item.Size:N0} B)" : item.SizeDisplay;
        CategoryBadgeText.Text = item.CategoryLabel;
        MetadataText.Text = $"更新日時: {item.ModifiedDate:yyyy/MM/dd HH:mm:ss}  |  属性: {item.AttributesDisplay}  |  拡張子: {item.Extension}";

        if (item.IsDirectory)
        {
            ComputeHashButton.Visibility = Visibility.Collapsed;
        }

        Loaded += (_, _) => LoadPreviewContent();
    }

    private void LoadPreviewContent()
    {
        try
        {
            if (_item.IsDirectory)
            {
                RenderDirectorySummary();
                return;
            }

            if (!File.Exists(_item.FullPath))
            {
                PreviewTextBox.Text = "⚠️ ファイルが存在しません。";
                return;
            }

            string ext = Path.GetExtension(_item.Name);

            // 1. 画像プレビュー
            if (ImageExtensions.Contains(ext))
            {
                if (TryRenderImage(_item.FullPath))
                    return;
            }

            // 2. テキスト・ソースコード・ログプレビュー (または先頭バイトがテキスト判定の場合)
            if (TextExtensions.Contains(ext) || IsLikelyTextFile(_item.FullPath))
            {
                RenderTextPreview(_item.FullPath);
                return;
            }

            // 3. バイナリ Hex + ASCII ダンププレビュー
            RenderBinaryHexPreview(_item.FullPath);
        }
        catch (Exception ex)
        {
            PreviewTextBox.Text = $"⚠️ プレビューの読み込み中にエラーが発生しました:\n{ex.Message}";
        }
    }

    private void RenderDirectorySummary()
    {
        var items = _scanner.GetDirectoryItems(_item.FullPath);
        int dirCount = items.Count(i => i.IsDirectory);
        int fileCount = items.Count(i => !i.IsDirectory);

        var sb = new StringBuilder();
        sb.AppendLine($"📁 フォルダ概要: {_item.FullPath}");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine($"・直下サブフォルダ数 : {dirCount:N0} 件");
        sb.AppendLine($"・直下ファイル数     : {fileCount:N0} 件");
        sb.AppendLine($"・既知合計サイズ     : {_item.SizeDisplay}");
        sb.AppendLine();
        sb.AppendLine("【直下の主要アイテム (最大 30 件)】");
        sb.AppendLine($"{"サイズ",-14} | {"種別",-16} | {"属性",-14} | 名前");
        sb.AppendLine(new string('-', 72));

        foreach (var child in items.OrderByDescending(i => i.SortableSize).Take(30))
        {
            sb.AppendLine($"{child.SizeDisplay,-14} | {child.CategoryLabel,-14} | {child.AttributesDisplay,-14} | {child.IconSymbol} {child.Name}");
        }

        PreviewTextBox.Text = sb.ToString();
        PreviewModeHintText.Text = "モード: フォルダ内容サマリー  |  Space / Esc で閉じる";
    }

    private bool TryRenderImage(string filePath)
    {
        try
        {
            var bmp = new BitmapImage();
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = fs;
                bmp.EndInit();
                bmp.Freeze();
            }

            PreviewImage.Source = bmp;
            PreviewTextBox.Visibility = Visibility.Collapsed;
            ImageScrollViewer.Visibility = Visibility.Visible;
            PreviewModeHintText.Text = $"モード: 画像プレビュー ({bmp.PixelWidth} x {bmp.PixelHeight} px)  |  Space / Esc で閉じる";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLikelyTextFile(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int len = (int)Math.Min(1024, fs.Length);
            if (len == 0) return true;

            byte[] buf = new byte[len];
            int read = fs.Read(buf, 0, len);
            int nullCount = 0;
            for (int i = 0; i < read; i++)
            {
                if (buf[i] == 0) nullCount++;
            }
            return nullCount == 0;
        }
        catch
        {
            return false;
        }
    }

    private void RenderTextPreview(string filePath)
    {
        const int maxBytes = 128 * 1024; // 最大 128 KB まで高速ストリーム読込
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int toRead = (int)Math.Min(maxBytes, fs.Length);
        byte[] buffer = new byte[toRead];
        int bytesRead = fs.Read(buffer, 0, toRead);

        string content = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        if (fs.Length > maxBytes)
        {
            content += $"\n\n... [以降省略: 全 {FileSystemItem.FormatBytes(fs.Length)} のうち先頭 128 KB を高速プレビュー表示中] ...";
        }

        PreviewTextBox.Text = content;
        PreviewModeHintText.Text = "モード: テキスト/ログ高速ストリームプレビュー (最大 128 KB)  |  Space / Esc で閉じる";
    }

    private void RenderBinaryHexPreview(string filePath)
    {
        const int maxHexBytes = 1024; // 先頭 1024 バイトの Hex + ASCII ダンプ
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        int toRead = (int)Math.Min(maxHexBytes, fs.Length);
        byte[] buf = new byte[toRead];
        int read = fs.Read(buf, 0, toRead);

        var sb = new StringBuilder();
        sb.AppendLine($"📦 バイナリファイル ヘッダーダンプ (先頭 {read} バイト / 全体 {FileSystemItem.FormatBytes(fs.Length)})");
        sb.AppendLine("Offset    00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F   ASCII");
        sb.AppendLine(new string('-', 76));

        for (int i = 0; i < read; i += 16)
        {
            sb.Append($"{i:X8}  ");
            var ascii = new StringBuilder(16);

            for (int j = 0; j < 16; j++)
            {
                if (i + j < read)
                {
                    byte b = buf[i + j];
                    sb.Append($"{b:X2} ");
                    ascii.Append(b is >= 32 and <= 126 ? (char)b : '.');
                }
                else
                {
                    sb.Append("   ");
                }

                if (j == 7) sb.Append(' ');
            }

            sb.Append("  ");
            sb.AppendLine(ascii.ToString());
        }

        PreviewTextBox.Text = sb.ToString();
        PreviewModeHintText.Text = "モード: バイナリ Hex ダンププレビュー  |  Space / Esc で閉じる";
    }

    private async void ComputeHashButton_Click(object sender, RoutedEventArgs e)
    {
        if (_item.IsDirectory || !File.Exists(_item.FullPath)) return;

        ComputeHashButton.IsEnabled = false;
        ComputeHashButton.Content = "計算中...";

        try
        {
            string hash = await Task.Run(() =>
            {
                using var fs = new FileStream(_item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sha = SHA256.Create();
                return Convert.ToHexString(sha.ComputeHash(fs));
            });

            MetadataText.Text = $"SHA-256: {hash}";
            ComputeHashButton.Content = "✓ ハッシュ計算完了";
        }
        catch (Exception ex)
        {
            MetadataText.Text = $"ハッシュ計算エラー: {ex.Message}";
            ComputeHashButton.Content = "🔑 SHA-256 ハッシュ計算";
            ComputeHashButton.IsEnabled = true;
        }
    }

    private void CopyPathButton_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_item.FullPath); } catch { }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Space)
        {
            e.Handled = true;
            Close();
        }
    }
}
