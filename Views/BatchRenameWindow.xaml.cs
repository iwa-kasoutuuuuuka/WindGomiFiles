using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Views;

public class RenamePreviewItem
{
    public required FileSystemItem SourceItem { get; init; }
    public string OriginalName { get; init; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public bool CanRename { get; set; }
}

public partial class BatchRenameWindow : Window
{
    private readonly List<FileSystemItem> _items;
    private List<RenamePreviewItem> _previews = [];
    public int RenamedCount { get; private set; }

    public BatchRenameWindow(IEnumerable<FileSystemItem> items)
    {
        InitializeComponent();
        _items = items.Where(i => !i.IsSystemProtected).ToList();
        UpdatePreview();
    }

    private void RuleInput_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        string find = FindTextBox?.Text ?? string.Empty;
        string replace = ReplaceTextBox?.Text ?? string.Empty;
        string prefix = PrefixTextBox?.Text ?? string.Empty;
        string suffix = SuffixTextBox?.Text ?? string.Empty;
        bool useRegex = UseRegexCheckBox?.IsChecked == true;
        bool addDate = AddDatePrefixCheckBox?.IsChecked == true;
        bool addNumber = AddNumberingCheckBox?.IsChecked == true;
        int startNum = int.TryParse(StartNumberTextBox?.Text, out int n) ? n : 1;

        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var previewList = new List<RenamePreviewItem>(_items.Count);
        var usedTargetPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int idx = 0;
        foreach (var item in _items)
        {
            string baseName = item.IsDirectory
                ? item.Name
                : Path.GetFileNameWithoutExtension(item.Name);
            string ext = item.IsDirectory
                ? string.Empty
                : Path.GetExtension(item.Name);

            string transformed = baseName;

            if (!string.IsNullOrEmpty(find))
            {
                if (useRegex)
                {
                    try
                    {
                        transformed = Regex.Replace(transformed, find, replace, RegexOptions.IgnoreCase);
                    }
                    catch
                    {
                        // 正規表現構文入力途中のエラーは無視
                    }
                }
                else
                {
                    transformed = transformed.Replace(find, replace, StringComparison.OrdinalIgnoreCase);
                }
            }

            string dateStr = addDate ? $"{item.ModifiedDate:yyyyMMdd}_" : string.Empty;
            string numStr = addNumber ? $"_{(startNum + idx):D3}" : string.Empty;
            idx++;

            string candidateName = $"{dateStr}{prefix}{transformed}{suffix}{numStr}{ext}";
            // 無効文字の除去
            candidateName = new string(candidateName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();

            string status = "変更なし";
            bool canRename = false;

            if (string.IsNullOrWhiteSpace(candidateName))
            {
                status = "⚠️ 無効な名前";
            }
            else if (!string.Equals(candidateName, item.Name, StringComparison.Ordinal))
            {
                string targetFullPath = Path.Combine(item.ParentPath, candidateName);
                bool isCaseOnlyChange = string.Equals(targetFullPath, item.FullPath, StringComparison.OrdinalIgnoreCase);

                if (!usedTargetPaths.Add(targetFullPath))
                {
                    status = "⚠️ リスト内で名前が重複しています";
                }
                else if (!isCaseOnlyChange && (File.Exists(targetFullPath) || Directory.Exists(targetFullPath)))
                {
                    status = "⚠️ 既存ファイルと名前が衝突しています";
                }
                else
                {
                    status = "✅ リネーム可能";
                    canRename = true;
                }
            }

            previewList.Add(new RenamePreviewItem
            {
                SourceItem = item,
                OriginalName = item.Name,
                NewName = candidateName,
                StatusText = status,
                CanRename = canRename
            });
        }

        _previews = previewList;
        if (PreviewGrid is not null)
        {
            PreviewGrid.ItemsSource = _previews;
        }
        if (SummaryText is not null)
        {
            SummaryText.Text = $"選択対象: {_items.Count:N0} 件  |  変更予定: {_previews.Count(p => p.CanRename):N0} 件";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ExecuteRenameButton_Click(object sender, RoutedEventArgs e)
    {
        var targets = _previews.Where(p => p.CanRename).ToList();
        if (targets.Count == 0)
        {
            MessageBox.Show("変更対象がありません。検索・置換や接頭辞・連番などの条件を入力してください。", "一括リネーム", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int success = 0;
        var errors = new List<string>();

        foreach (var p in targets)
        {
            try
            {
                string destPath = Path.Combine(p.SourceItem.ParentPath, p.NewName);
                if (p.SourceItem.IsDirectory)
                {
                    Directory.Move(p.SourceItem.FullPath, destPath);
                }
                else
                {
                    File.Move(p.SourceItem.FullPath, destPath);
                }
                p.SourceItem.Name = p.NewName;
                p.SourceItem.FullPath = destPath;
                success++;
            }
            catch (Exception ex)
            {
                errors.Add($"{p.OriginalName}: {ex.Message}");
            }
        }

        RenamedCount = success;
        if (errors.Count > 0)
        {
            MessageBox.Show(
                $"{success} 件のリネームに成功しましたが、一部でエラーが発生しました:\n" + string.Join("\n", errors.Take(5)),
                "一括リネーム結果",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        DialogResult = success > 0;
        Close();
    }
}
