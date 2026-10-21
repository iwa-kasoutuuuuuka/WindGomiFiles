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
        "usrclass.dat",
        "usrclass.dat.log1",
        "usrclass.dat.log2",
        "desktop.ini"
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

    private static readonly string WindowsDir = NormalizeDir(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"C:\Windows");
    private static readonly string ProgramFilesDir = NormalizeDir(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"C:\Program Files");
    private static readonly string ProgramFilesX86Dir = NormalizeDir(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"C:\Program Files (x86)");
    private static readonly string ProgramDataDir = NormalizeDir(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"C:\ProgramData");
    private static readonly string UserProfileDir = NormalizeDir(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), string.Empty);

    private static readonly HashSet<string> ProtectedContainerDirs = BuildProtectedContainerDirs();

    private static string NormalizeDir(string path, string fallback)
    {
        string target = string.IsNullOrWhiteSpace(path) ? fallback : path;
        if (string.IsNullOrWhiteSpace(target)) return string.Empty;
        try
        {
            return Path.GetFullPath(target).TrimEnd('\\');
        }
        catch
        {
            return target.TrimEnd('\\');
        }
    }

    private static HashSet<string> BuildProtectedContainerDirs()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            WindowsDir,
            ProgramFilesDir,
            ProgramFilesX86Dir,
            ProgramDataDir,
            @"C:\Users",
            @"C:\Recovery",
            Path.Combine(WindowsDir, "Temp"),
            Path.Combine(WindowsDir, @"SoftwareDistribution\Download"),
            Path.Combine(WindowsDir, "Prefetch"),
            Path.Combine(WindowsDir, "Minidump"),
            Path.Combine(WindowsDir, "LiveKernelReports")
        };

        if (!string.IsNullOrEmpty(UserProfileDir))
        {
            set.Add(UserProfileDir);
            set.Add(Path.Combine(UserProfileDir, "AppData"));
            set.Add(Path.Combine(UserProfileDir, @"AppData\Local"));
            set.Add(Path.Combine(UserProfileDir, @"AppData\Roaming"));
            set.Add(Path.Combine(UserProfileDir, @"AppData\LocalLow"));
            set.Add(Path.Combine(UserProfileDir, @"AppData\Local\Temp"));
            set.Add(Path.Combine(UserProfileDir, "Desktop"));
            set.Add(Path.Combine(UserProfileDir, "Downloads"));
            set.Add(Path.Combine(UserProfileDir, "Documents"));
            set.Add(Path.Combine(UserProfileDir, "Pictures"));
            set.Add(Path.Combine(UserProfileDir, "Music"));
            set.Add(Path.Combine(UserProfileDir, "Videos"));
        }

        string tempRoot = Path.GetTempPath().TrimEnd('\\');
        if (!string.IsNullOrEmpty(tempRoot))
        {
            set.Add(tempRoot);
        }

        set.Remove(string.Empty);
        return set;
    }

    /// <summary>
    /// 指定したパスがOS起動やシステムの根幹に関わる保護対象かどうかを判定します。
    /// （C:\Windows\Temp や SoftwareDistribution\Download 内のアイテム等の安全な一時領域は保護から除外します）
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

            // 3. System Volume Information / Recovery / $Recycle.Bin / $WinREAgent
            if (fileName.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("$WinREAgent", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\Recovery", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(@"C:\System Volume Information", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 4. 重要なルートディレクトリ（器自体）は丸ごと削除を禁止
            if (isDirectory && ProtectedContainerDirs.Contains(normalized))
            {
                return true;
            }

            // 5. C:\Windows 配下の判定（ホワイトリスト優先、それ以外の C:\Windows 配下はすべて保護）
            if (normalized.Equals(WindowsDir, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(WindowsDir + @"\", StringComparison.OrdinalIgnoreCase))
            {
                // 安全に削除可能な Windows 一時・キャッシュ・ダンプ領域の中身のみ削除許可
                if (normalized.StartsWith(Path.Combine(WindowsDir, @"Temp\"), StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(Path.Combine(WindowsDir, @"SoftwareDistribution\Download\"), StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(Path.Combine(WindowsDir, @"Prefetch\"), StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(Path.Combine(WindowsDir, @"LiveKernelReports\"), StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith(Path.Combine(WindowsDir, @"Minidump\"), StringComparison.OrdinalIgnoreCase) ||
                    normalized.Equals(Path.Combine(WindowsDir, "MEMORY.DMP"), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // 上記ホワイトリスト以外の C:\Windows 配下（System32, explorer.exe, INF, Drivers 等すべて）は完全保護
                return true;
            }

            // 6. Program Files / Program Files (x86) 直下のシステム重要アプリ（WindowsApps / Microsoft / Common Files 等）の保護
            if (normalized.StartsWith(ProgramFilesDir + @"\", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(ProgramFilesX86Dir + @"\", StringComparison.OrdinalIgnoreCase))
            {
                if (normalized.Contains(@"\WindowsApps", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(@"\Common Files", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(@"\Windows Defender", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(@"\Reference Assemblies", StringComparison.OrdinalIgnoreCase) ||
                    normalized.Contains(@"\dotnet", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
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

        // Program Files 配下の通常アプリケーションファイルが誤って「ゴミ・キャッシュ候補」に分類されるのを防止
        bool isUnderProgramFiles =
            (!string.IsNullOrEmpty(ProgramFilesDir) && fullPath.StartsWith(ProgramFilesDir + @"\", StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(ProgramFilesX86Dir) && fullPath.StartsWith(ProgramFilesX86Dir + @"\", StringComparison.OrdinalIgnoreCase));

        if (isUnderProgramFiles)
        {
            return (ItemCategory.Normal, "通常 (アプリ領域)", false);
        }

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

        // 4. アプリ・ブラウザ・GPUキャッシュ (ディレクトリ境界で厳密に判定)
        if (lowerPath.Contains(@"\cache\") ||
            lowerPath.Contains(@"\caches\") ||
            lowerPath.Contains(@"\d3dscache") ||
            lowerPath.Contains(@"\shadercache") ||
            lowerPath.Contains(@"\gpucache") ||
            lowerPath.Contains(@"\code cache") ||
            lowerPath.Contains(@"\inetcache") ||
            lowerPath.Contains(@"\cachestorage") ||
            lowerName is "cache" or "caches" or "gpucache" or "shadercache" or "code cache" or "d3dscache")
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
