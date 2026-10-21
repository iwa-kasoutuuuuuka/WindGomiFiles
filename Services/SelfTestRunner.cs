using System.IO;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Services;

/// <summary>
/// WinGomiExplorer 内蔵の自動動作検証・脆弱性回帰テストランナー (--self-test)。
/// </summary>
public static class SelfTestRunner
{
    public static int RunAllTests()
    {
        DiagnosticsService.LogInfo("SelfTest", "=== Starting WinGomiExplorer Automated Verification & Security Suite ===");
        int passed = 0;

        void Assert(bool condition, string testName)
        {
            if (!condition)
            {
                string msg = $"[FAIL] {testName}";
                DiagnosticsService.LogError("SelfTest", msg);
                throw new InvalidOperationException(msg);
            }
            passed++;
            DiagnosticsService.LogInfo("SelfTest", $"[PASS #{passed}] {testName}");
        }

        try
        {
            // 1. システム保護・脆弱性防止ルールの検証
            Assert(SystemProtectionService.IsSystemProtectedPath(@"C:\", true), "Dry-check: Root C:\\ is protected");
            Assert(SystemProtectionService.IsSystemProtectedPath(@"C:\Windows", true), "Dry-check: C:\\Windows container is protected");
            Assert(SystemProtectionService.IsSystemProtectedPath(@"C:\Windows\System32\kernel32.dll", false), "Dry-check: System32 file is protected");
            Assert(SystemProtectionService.IsSystemProtectedPath(@"C:\Windows\explorer.exe", false), "Dry-check: C:\\Windows\\explorer.exe is protected");
            Assert(SystemProtectionService.IsSystemProtectedPath(@"C:\Windows\INF\setupapi.dev.log", false), "Dry-check: C:\\Windows\\INF is protected");
            Assert(SystemProtectionService.IsSystemProtectedPath(@"C:\pagefile.sys", false), "Dry-check: pagefile.sys is protected");
            Assert(SystemProtectionService.IsSystemProtectedPath(@"C:\Users\TestUser\ntuser.dat", false), "Dry-check: ntuser.dat is protected");
            Assert(SystemProtectionService.IsSystemProtectedPath(@"C:\Windows\Temp", true), "Dry-check: C:\\Windows\\Temp container itself is protected");
            Assert(!SystemProtectionService.IsSystemProtectedPath(@"C:\Windows\Temp\sample_junk.tmp", false), "Dry-check: Files inside C:\\Windows\\Temp are deletable");
            Assert(!SystemProtectionService.IsSystemProtectedPath(@"C:\Windows\SoftwareDistribution\Download\pkg.cab", false), "Dry-check: Files inside SoftwareDistribution\\Download are deletable");

            var pfCheck = SystemProtectionService.ClassifyItem(@"C:\Program Files\VendorApp\cache_helper.dll", "cache_helper.dll", ".dll", false);
            Assert(!SystemProtectionService.IsJunkCandidate(pfCheck.Category), "Security: Program Files DLL with 'cache' in name is NOT misclassified as junk");

            // 2. サンドボックス環境での隠し/システム属性取得、ジャンクション無限ループ防止、容量計算、耐障害性削除の検証
            string sandbox = Path.Combine(Path.GetTempPath(), "WinGomiSelfTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);

            try
            {
                string subA = Path.Combine(sandbox, "SubFolderA");
                Directory.CreateDirectory(subA);

                string fileNormal = Path.Combine(subA, "normal.log");
                string fileHiddenSys = Path.Combine(subA, "hidden_sys.tmp");
                string fileLocked = Path.Combine(subA, "locked_in_use.tmp");

                File.WriteAllBytes(fileNormal, new byte[20480]);    // 20 KB
                File.WriteAllBytes(fileHiddenSys, new byte[30720]); // 30 KB
                File.SetAttributes(fileHiddenSys, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
                File.WriteAllBytes(fileLocked, new byte[10240]);    // 10 KB

                // ジャンクション (ReparsePoint) 作成によるループ・二重計上耐性テスト
                string junctionPath = Path.Combine(sandbox, "JunctionLoop");
                try
                {
                    Directory.CreateSymbolicLink(junctionPath, subA);
                }
                catch
                {
                    // 権限により CreateSymbolicLink が不可の場合は cmd mklink /J を使用
                    var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{subA}\"")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(psi)?.WaitForExit();
                }

                var scanner = new DiskScannerService();

                // 2a. 隠しファイル・システムファイル・拡張子取得検証
                var items = scanner.GetDirectoryItems(subA);
                Assert(items.Count == 3, $"GetDirectoryItems found all 3 files including Hidden+System+ReadOnly (Actual={items.Count})");
                Assert(items.Any(i => i.Name == "hidden_sys.tmp" && i.IsHidden && i.IsSystem && i.IsReadOnly), "Hidden+System+ReadOnly flags accurately parsed");

                // 2b. フルスキャン & ジャンクション二重計上防止検証
                var scanResult = scanner.ScanLargeItemsAsync(sandbox, 1024, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert(scanResult.TotalScannedFiles == 3, $"ScanLargeItemsAsync skipped junction recursion and counted exact 3 files (Actual={scanResult.TotalScannedFiles})");
                Assert(scanResult.TotalScannedBytes == 61440, $"TotalScannedBytes is exact 61,440 bytes (Actual={scanResult.TotalScannedBytes})");

                // 3. 親子重複選択時の解放予定サイズ排除検証
                var parentItem = new FileSystemItem { Name = "SubFolderA", FullPath = subA, IsDirectory = true, Size = 61440 };
                var childItem = new FileSystemItem { Name = "normal.log", FullPath = fileNormal, IsDirectory = false, Size = 20480 };
                long nonOverlapping = ExplorerTabViewModel.CalculateNonOverlappingBytes([parentItem, childItem]);
                Assert(nonOverlapping == 61440, $"CalculateNonOverlappingBytes deduplicated parent/child selection (Expected=61440, Actual={nonOverlapping})");

                // 4. 耐障害性完全削除検証（1ファイルを排他ロックした状態で ReadOnly+Hidden+System を含むフォルダを削除）
                using (var lockStream = new FileStream(fileLocked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var delReport = FileDeletionService.PermanentlyDeleteItemsAsync([parentItem], scanner).GetAwaiter().GetResult();
                    Assert(!File.Exists(fileNormal), "Normal file permanently deleted");
                    Assert(!File.Exists(fileHiddenSys), "Hidden+System+ReadOnly file attributes cleared and permanently deleted");
                    Assert(File.Exists(fileLocked), "In-use locked file safely skipped without aborting sibling deletion");
                    Assert(delReport.FreedBytes == 51200, $"FreedBytes accurately reported 51,200 bytes (Actual={delReport.FreedBytes})");
                }
            }
            finally
            {
                try
                {
                    string junctionPath = Path.Combine(sandbox, "JunctionLoop");
                    if (Directory.Exists(junctionPath))
                        Directory.Delete(junctionPath, false);
                    if (Directory.Exists(sandbox))
                        Directory.Delete(sandbox, true);
                }
                catch { }
            }

            DiagnosticsService.LogInfo("SelfTest", $"=== ALL {passed} VERIFICATION & SECURITY TESTS PASSED SUCCESSFULLY ===");
            return 0;
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogError("SelfTest", "Self-test suite failed!", ex);
            return 1;
        }
    }
}
