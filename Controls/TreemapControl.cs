using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Controls;

/// <summary>
/// ディスク占有率を面積比例のブロックで可視化する高速カスタム Treemap コントロール。
/// </summary>
public class TreemapControl : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<FileSystemItem>),
            typeof(TreemapControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public IEnumerable<FileSystemItem>? ItemsSource
    {
        get => (IEnumerable<FileSystemItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public event Action<FileSystemItem>? ItemClicked;
    public event Action<FileSystemItem>? ItemDoubleClicked;

    private readonly List<(Rect Bounds, FileSystemItem Item)> _blocks = [];
    private FileSystemItem? _hoveredItem;
    private readonly ToolTip _toolTip = new();

    private static readonly Typeface UiTypeface = new(new FontFamily("Yu Gothic UI, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Pen BlockBorderPen = CreateFrozenPen(Color.FromRgb(0x0B, 0x11, 0x20), 1.5);
    private static readonly Pen HoverBorderPen = CreateFrozenPen(Color.FromRgb(0xF8, 0xFA, 0xFC), 2.2);

    public TreemapControl()
    {
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        ToolTip = _toolTip;
        _toolTip.IsOpen = false;
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreemapControl ctrl)
        {
            ctrl._hoveredItem = null;
            ctrl.InvalidateVisual();
        }
    }

    public void RefreshTreemap()
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        Rect totalRect = new(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)), null, totalRect);

        _blocks.Clear();

        if (totalRect.Width < 20 || totalRect.Height < 20 || ItemsSource is null)
            return;

        var validItems = ItemsSource
            .Where(i => i.Size > 0)
            .OrderByDescending(i => i.Size)
            .Take(48)
            .ToList();

        if (validItems.Count == 0)
        {
            DrawEmptyMessage(dc, totalRect, "容量計算完了後にツリーマップ（面積グラフ）が表示されます");
            return;
        }

        double totalSize = validItems.Sum(i => (double)i.Size);
        if (totalSize <= 0) return;

        ComputeTreemapLayout(validItems, 0, validItems.Count - 1, totalRect);

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        foreach (var (rect, item) in _blocks)
        {
            if (rect.Width < 2 || rect.Height < 2) continue;

            Brush fill = GetBlockBrush(item);
            bool isHovered = ReferenceEquals(item, _hoveredItem);
            dc.DrawRoundedRectangle(fill, isHovered ? HoverBorderPen : BlockBorderPen, rect, 3, 3);

            // 十分な広さがあるブロックにはファイル名とサイズを描画
            if (rect.Width >= 46 && rect.Height >= 22)
            {
                dc.PushClip(new RectangleGeometry(new Rect(rect.X + 3, rect.Y + 2, Math.Max(0, rect.Width - 6), Math.Max(0, rect.Height - 4))));
                try
                {
                    string label = $"{item.IconSymbol} {item.Name}";
                    var ftName = new FormattedText(
                        label,
                        CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight,
                        UiTypeface,
                        11.5,
                        Brushes.White,
                        dpi)
                    {
                        MaxTextWidth = Math.Max(10, rect.Width - 8),
                        MaxLineCount = 1,
                        Trimming = TextTrimming.CharacterEllipsis
                    };

                    dc.DrawText(ftName, new Point(rect.X + 5, rect.Y + 3));

                    if (rect.Height >= 38)
                    {
                        var ftSize = new FormattedText(
                            item.SizeDisplay,
                            CultureInfo.CurrentUICulture,
                            FlowDirection.LeftToRight,
                            UiTypeface,
                            10.5,
                            new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                            dpi)
                        {
                            MaxTextWidth = Math.Max(10, rect.Width - 8),
                            MaxLineCount = 1,
                            Trimming = TextTrimming.CharacterEllipsis
                        };

                        dc.DrawText(ftSize, new Point(rect.X + 5, rect.Y + 19));
                    }
                }
                finally
                {
                    dc.Pop();
                }
            }
        }
    }

    private void ComputeTreemapLayout(IReadOnlyList<FileSystemItem> items, int start, int end, Rect bounds)
    {
        if (start > end || bounds.Width <= 1 || bounds.Height <= 1)
            return;

        if (start == end)
        {
            _blocks.Add((bounds, items[start]));
            return;
        }

        double total = 0;
        for (int i = start; i <= end; i++)
            total += items[i].Size;

        if (total <= 0) return;

        // 合計サイズがおおよそ半分になる分割点を探す (Balanced Binary Treemap)
        double half = total / 2.0;
        double accum = 0;
        int mid = start;

        for (int i = start; i < end; i++)
        {
            accum += items[i].Size;
            mid = i;
            if (accum >= half) break;
        }

        double ratio = Math.Clamp(accum / total, 0.05, 0.95);

        if (bounds.Width >= bounds.Height)
        {
            double w1 = bounds.Width * ratio;
            Rect r1 = new(bounds.X, bounds.Y, w1, bounds.Height);
            Rect r2 = new(bounds.X + w1, bounds.Y, Math.Max(0, bounds.Width - w1), bounds.Height);
            ComputeTreemapLayout(items, start, mid, r1);
            ComputeTreemapLayout(items, mid + 1, end, r2);
        }
        else
        {
            double h1 = bounds.Height * ratio;
            Rect r1 = new(bounds.X, bounds.Y, bounds.Width, h1);
            Rect r2 = new(bounds.X, bounds.Y + h1, bounds.Width, Math.Max(0, bounds.Height - h1));
            ComputeTreemapLayout(items, start, mid, r1);
            ComputeTreemapLayout(items, mid + 1, end, r2);
        }
    }

    private static void DrawEmptyMessage(DrawingContext dc, Rect bounds, string message)
    {
        double dpi = 1.0;
        var ft = new FormattedText(
            message,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            UiTypeface,
            12.5,
            new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            dpi);

        dc.DrawText(ft, new Point(
            Math.Max(8, (bounds.Width - ft.Width) / 2),
            Math.Max(8, (bounds.Height - ft.Height) / 2)));
    }

    private static Brush GetBlockBrush(FileSystemItem item)
    {
        Color c1 = item.Category switch
        {
            ItemCategory.TempOrJunk => Color.FromRgb(0xD9, 0x77, 0x06),         // Amber
            ItemCategory.Cache => Color.FromRgb(0x02, 0x84, 0xC7),              // Sky Blue
            ItemCategory.DevCache => Color.FromRgb(0x7C, 0x3A, 0xED),           // Violet
            ItemCategory.LogOrDump => Color.FromRgb(0xDB, 0x27, 0x77),          // Pink
            ItemCategory.InstallerOrArchive => Color.FromRgb(0x05, 0x96, 0x69), // Emerald
            ItemCategory.SystemProtected => Color.FromRgb(0x47, 0x55, 0x69),    // Slate Shield
            _ => item.IsDirectory
                ? Color.FromRgb(0x25, 0x63, 0xEB)                               // Folder Royal Blue
                : Color.FromRgb(0x3B, 0x82, 0xF6)                               // File Blue
        };

        Color c2 = Color.FromRgb(
            (byte)(c1.R * 0.68),
            (byte)(c1.G * 0.68),
            (byte)(c1.B * 0.68));

        var brush = new LinearGradientBrush(c1, c2, 45.0);
        brush.Freeze();
        return brush;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point pt = e.GetPosition(this);
        var hit = _blocks.FirstOrDefault(b => b.Bounds.Contains(pt)).Item;

        if (!ReferenceEquals(hit, _hoveredItem))
        {
            _hoveredItem = hit;
            if (hit is not null)
            {
                _toolTip.Content = $"{hit.IconSymbol} {hit.Name}\n容量: {hit.SizeDisplay}\n分類: {hit.CategoryLabel}\nパス: {hit.FullPath}\n(クリックで選択 / ダブルクリックで開く)";
                _toolTip.IsOpen = true;
            }
            else
            {
                _toolTip.IsOpen = false;
            }
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredItem is not null)
        {
            _hoveredItem = null;
            _toolTip.IsOpen = false;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Point pt = e.GetPosition(this);
        var hit = _blocks.FirstOrDefault(b => b.Bounds.Contains(pt)).Item;
        if (hit is null) return;

        if (e.ClickCount == 2)
        {
            ItemDoubleClicked?.Invoke(hit);
        }
        else
        {
            ItemClicked?.Invoke(hit);
        }
    }

    private static Pen CreateFrozenPen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
