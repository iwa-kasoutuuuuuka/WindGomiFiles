using System.IO;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Services;

public static class SystemProtectionService
{
    private static readonly HashSet<string> ProtectedRootFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys",
        "hiberfil.sys",
        "swapfile.sys",
        "bootmgr",
        "bootnxt",
        "ntldr",
        "ntdetect.com",
        "ntuser.dat",
        "ntuser.dat.log1",
        "ntuser.dat.log2",
        "usrclass.dat"
    };

    private static readonly HashSet<string> TempExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".temp", ".bak", ".old", ".orig", ".dmp_old", ".chk"
    };

    private static readonly HashSet<string> LogDumpExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".log", ".dmp", ".mdmp", ".hdmp", ".etl", ".evtx", ".wer"
    };

    private static readonly HashSet<string> InstallerArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".msi", ".msix", ".cab", ".img"
    };

    /// <summary>
    /// 指定したパスがOS起動やシステムの根幹に関わる保護対象かどうかを判定します。
    /// （C:\Windows\Temp や SoftwareDistribution\Download などの安全な一時領域は保護から除外します）
    /// </summary>
    public static bool IsSystemProtectedPath(string fullPath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return true;

        try
        {
            string normalized = Path.GetFullPath(fullPath).TrimEnd('\\');

            // 1. ドライブルート自体 (例: "C:")
            if (normalized.Length <= 3 && normalized.EndsWith(':'))
                return true;

            string fileName = Path.GetFileName(normalized);

            // 2. 特殊なOSシステムファイル (pagefile.sys, hiberfil.sys, ntuser.dat 等)
            if (ProtectedRootFileNames.Contains(fileName))
                return true;

            // 3. System Volume Information / Recovery / $Recycle.Bin 自体
            if (fileName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("$WinREAgent", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Recovery", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 4. C:\Windows 内の安全な削除可能領域は「保護対象外（削除OK）」とする
            if (normalized.StartsWith(@"C:\Windows\Temp\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\SoftwareDistribution\Download\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\Prefetch\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\LiveKernelReports\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\Minidump\", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(@"C:\Windows\MEMORY.DMP", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 5. C:\Windows 自体およびシステムコア配下はすべて保護
            if (normalized.Equals(@"C:\Windows", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\System32", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\SysWOW64", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\WinSxS", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\Boot", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\SystemApps", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\servicing", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\Fonts", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\Microsoft.NET", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Windows\SystemResources", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 6. 重要なルートディレクトリ自体（丸ごと消すと危険な器）
            if (isDirectory)
            {
                if (normalized.Equals(@"C:\Program Files", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(@"C:\Program Files (x86)", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(@"C:\ProgramData", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(@"C:\Users", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(@"C:\Windows\Temp", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // ユーザープロファイル直下 (C:\Users\Username) や AppData 自体も器としては保護
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
                if (!string.IsNullOrEmpty(userProfile))
                {
                    if (normalized.Equals(userProfile, StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(Path.Combine(userProfile, "AppData"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(Path.Combine(userProfile, @"AppData\Local"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(Path.Combine(userProfile, @"AppData\Roaming"), StringComparison.OrdinalIgnoreCase) ||
                        normalized.Equals(Path.Combine(userProfile, @"AppData\LocalLow"), StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// パスと属性からアイテムのカテゴリと表示ラベルを判定します。
    /// </summary>
    public static (ItemCategory Category, string Label, bool IsProtected) ClassifyItem(string fullPath, string name, string extension, bool isDirectory)
    {
        bool isProtected = IsSystemProtectedPath(fullPath, isDirectory);
        if (isProtected)
        {
            return (ItemCategory.SystemProtected, "🛡️ システム保護", true);
        }

        string lowerPath = fullPath.ToLowerInvariant();
        string lowerName = name.ToLowerInvariant();

        // 1. 一時ファイル / Windows.old 等
        if (lowerPath.Contains(@"\temp\") ||
            lowerPath.Contains(@"\tmp\") ||
            lowerPath.Contains(@"\windows.old") ||
            lowerPath.Contains(@"\$windows.~bt") ||
            lowerPath.Contains(@"\$windows.~ws") ||
            lowerPath.Contains(@"\softwaredistribution\download") ||
            lowerName is "windows.old" or "$windows.~bt" or "thumbs.db" ||
            (!isDirectory && TempExtensions.Contains(extension)))
        {
            return (ItemCategory.TempOrJunk, "🧹 一時・不要データ", false);
        }

        // 2. ログ・クラッシュダンプ
        if (lowerPath.Contains(@"\crashdumps") ||
            lowerPath.Contains(@"\livekernelreports") ||
            lowerPath.Contains(@"\minidump") ||
            lowerPath.Contains(@"\wer\report") ||
            lowerName.Equals("memory.dmp", StringComparison.OrdinalIgnoreCase) ||
            (!isDirectory && LogDumpExtensions.Contains(extension)))
        {
            return (ItemCategory.LogOrDump, "📋 ログ・ダンプ", false);
        }

        // 3. 開発ツールキャッシュ・ビルド生成物
        if (lowerPath.Contains(@"\node_modules") ||
            lowerPath.Contains(@"\.nuget\packages") ||
            lowerPath.Contains(@"\pip\cache") ||
            lowerPath.Contains(@"\.cargo\registry") ||
            lowerPath.Contains(@"\.cargo\git") ||
            lowerPath.Contains(@"\.gradle\caches") ||
            lowerPath.Contains(@"\package cache") ||
            lowerName is "node_modules" or ".venv" or "venv" or "__pycache__")
        {
            return (ItemCategory.DevCache, "🔧 開発キャッシュ", false);
        }

        // 4. アプリ・ブラウザ・GPUキャッシュ
        if (lowerPath.Contains(@"cache") ||
            lowerPath.Contains(@"\d3dscache") ||
            lowerPath.Contains(@"\shadercache") ||
            lowerPath.Contains(@"\gpucache") ||
            lowerPath.Contains(@"\code cache") ||
            lowerPath.Contains(@"\inetcache"))
        {
            return (ItemCategory.Cache, "⚡ キャッシュ", false);
        }

        // 5. インストーラー・アーカイブ・ダウンロード放置
        if (!isDirectory && (InstallerArchiveExtensions.Contains(extension) ||
            (lowerPath.Contains(@"\downloads\") && extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))))
        {
            return (ItemCategory.InstallerOrArchive, "📦 圧縮/インストーラ", false);
        }

        if (lowerPath.Contains(@"\downloads\"))
        {
            return (ItemCategory.InstallerOrArchive, "📥 ダウンロード", false);
        }

        return (ItemCategory.Normal, "通常", false);
    }

    public static bool IsJunkCandidate(ItemCategory category)
    {
        return category is ItemCategory.TempOrJunk
            or ItemCategory.Cache
            or ItemCategory.LogOrDump
            or ItemCategory.DevCache
            or ItemCategory.InstallerOrArchive;
    }
}
