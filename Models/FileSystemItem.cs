using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace WinGomiExplorer.Models;

public enum ItemCategory
{
    Normal,
    TempOrJunk,
    Cache,
    LogOrDump,
    InstallerOrArchive,
    DevCache,
    SystemProtected
}

public class FileSystemItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private long _size = -1;
    private double _sizeRatio;
    private bool _isCalculatingSize;
    private int _fileCount;

    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string ParentPath => Path.GetDirectoryName(FullPath) ?? string.Empty;
    public string Extension { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public DateTime ModifiedDate { get; set; }
    public FileAttributes Attributes { get; set; }

    public bool IsHidden => (Attributes & FileAttributes.Hidden) != 0;
    public bool IsSystem => (Attributes & FileAttributes.System) != 0;
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;
    public bool IsReparsePoint => (Attributes & FileAttributes.ReparsePoint) != 0;

    public ItemCategory Category { get; set; } = ItemCategory.Normal;
    public string CategoryLabel { get; set; } = "通常";
    public bool IsSystemProtected { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (IsSystemProtected && value)
            {
                // システム保護対象は一括チェック対象から除外
                return;
            }
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// バイト単位のサイズ。未計算は -1、計算中は -2
    /// </summary>
    public long Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeDisplay));
                OnPropertyChanged(nameof(SortableSize));
            }
        }
    }

    /// <summary>
    /// ソート用のサイズ値（未計算や計算中は 0 として扱う）
    /// </summary>
    public long SortableSize => _size >= 0 ? _size : 0;

    public bool IsCalculatingSize
    {
        get => _isCalculatingSize;
        set
        {
            if (_isCalculatingSize != value)
            {
                _isCalculatingSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeDisplay));
            }
        }
    }

    public int FileCount
    {
        get => _fileCount;
        set
        {
            if (_fileCount != value)
            {
                _fileCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeDisplay));
            }
        }
    }

    /// <summary>
    /// 0.0 ～ 1.0 の相対容量バー表示用割合
    /// </summary>
    public double SizeRatio
    {
        get => _sizeRatio;
        set
        {
            if (Math.Abs(_sizeRatio - value) > 0.001)
            {
                _sizeRatio = Math.Clamp(value, 0.0, 1.0);
                OnPropertyChanged();
            }
        }
    }

    public string IconSymbol
    {
        get
        {
            if (IsSystemProtected) return "🛡️";
            if (IsDirectory)
            {
                if (IsReparsePoint) return "🔗";
                if (IsHidden) return "👻";
                return "📁";
            }
            return Category switch
            {
                ItemCategory.TempOrJunk => "🧹",
                ItemCategory.Cache => "⚡",
                ItemCategory.LogOrDump => "📋",
                ItemCategory.InstallerOrArchive => "📦",
                ItemCategory.DevCache => "🔧",
                _ => IsHidden ? "📄" : "📄"
            };
        }
    }

    public string AttributesDisplay
    {
        get
        {
            var list = new List<string>(4);
            if (IsHidden) list.Add("隠し(H)");
            if (IsSystem) list.Add("システム(S)");
            if (IsReadOnly) list.Add("読取専用(R)");
            if (IsReparsePoint) list.Add("リンク(L)");
            return list.Count > 0 ? string.Join(" ", list) : "-";
        }
    }

    public string SizeDisplay
    {
        get
        {
            if (IsDirectory && IsReparsePoint && _size < 0)
            {
                return "リンク (対象外)";
            }
            if (_isCalculatingSize || _size == -2)
            {
                return "計算中...";
            }
            if (_size < 0)
            {
                return "未計算";
            }
            return FormatBytes(_size);
        }
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 0) return "-";
        if (bytes == 0) return "0 B";

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:N0} {units[unitIndex]}"
            : $"{value:N2} {units[unitIndex]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
