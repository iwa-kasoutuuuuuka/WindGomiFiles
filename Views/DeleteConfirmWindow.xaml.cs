using System.Windows;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Views;

public partial class DeleteConfirmWindow : Window
{
    public DeleteConfirmWindow(IReadOnlyList<FileSystemItem> itemsToDelete)
    {
        InitializeComponent();

        var deletable = itemsToDelete.Where(i => !i.IsSystemProtected).ToList();
        long totalBytes = ExplorerTabViewModel.CalculateNonOverlappingBytes(deletable);

        SummaryText.Text = $"削除対象: {deletable.Count:N0} 件  |  解放予定の合計容量: {FileSystemItem.FormatBytes(totalBytes)}";
        TargetGrid.ItemsSource = deletable;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
