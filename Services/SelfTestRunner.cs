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

            // 2. サンドボックス環境でのスキャン・重複検出・空フォルダ検出・耐障害性削除の検証
            string sandbox = Path.Combine(Path.GetTempPath(), "WinGomiSelfTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);

            try
            {
                string subA = Path.Combine(sandbox, "SubFolderA");
                string emptyDir = Path.Combine(sandbox, "EmptySubFolder");
                Directory.CreateDirectory(subA);
                Directory.CreateDirectory(emptyDir);

                string fileNormal = Path.Combine(subA, "normal.log");
                string fileDupCopy = Path.Combine(subA, "normal_copy.log");
                string fileHiddenSys = Path.Combine(subA, "hidden_sys.tmp");
                string fileLocked = Path.Combine(subA, "locked_in_use.tmp");
                string fileZeroByte = Path.Combine(subA, "zero_byte.tmp");

                byte[] samplePayload = new byte[20480];
                new Random(42).NextBytes(samplePayload);

                File.WriteAllBytes(fileNormal, samplePayload);       // 20 KB
                File.WriteAllBytes(fileDupCopy, samplePayload);      // 20 KB (同一内容の重複コピー)
                File.WriteAllBytes(fileHiddenSys, new byte[30720]);  // 30 KB
                File.SetAttributes(fileHiddenSys, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
                File.WriteAllBytes(fileLocked, new byte[10240]);     // 10 KB
                File.WriteAllBytes(fileZeroByte, []);                // 0 B

                // ジャンクション (ReparsePoint) 作成によるループ・二重計上耐性テスト
                string junctionPath = Path.Combine(sandbox, "JunctionLoop");
                try
                {
                    Directory.CreateSymbolicLink(junctionPath, subA);
                }
                catch
                {
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
                Assert(items.Count == 5, $"GetDirectoryItems found all 5 files including Hidden+System+ReadOnly+ZeroByte (Actual={items.Count})");
                Assert(items.Any(i => i.Name == "hidden_sys.tmp" && i.IsHidden && i.IsSystem && i.IsReadOnly), "Hidden+System+ReadOnly flags accurately parsed");

                // 2b. フルスキャン & ジャンクション二重計上防止検証
                var scanResult = scanner.ScanLargeItemsAsync(sandbox, 1024, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert(scanResult.TotalScannedFiles == 5, $"ScanLargeItemsAsync skipped junction recursion and counted exact 5 files (Actual={scanResult.TotalScannedFiles})");
                Assert(scanResult.TotalScannedBytes == 81920, $"TotalScannedBytes is exact 81,920 bytes (Actual={scanResult.TotalScannedBytes})");

                // 2c. 重複ファイル検出 (DuplicateDetectorService) 検証
                var duplicates = DuplicateDetectorService.FindDuplicateFilesAsync(scanResult.LargeFiles, 1024, null, CancellationToken.None).GetAwaiter().GetResult();
                Assert(duplicates.Count == 2, $"FindDuplicateFilesAsync detected exact 2 duplicate items (1 original + 1 copy, Actual={duplicates.Count})");
                Assert(duplicates.Count(d => d.IsSelected) == 1, "Only the duplicate copy is pre-selected for deletion while keeping the original safe");

                // 2d. 空フォルダ & 0バイトファイル検出検証
                var emptyItems = DuplicateDetectorService.FindEmptyFoldersAndZeroByteFilesAsync(sandbox, CancellationToken.None).GetAwaiter().GetResult();
                Assert(emptyItems.Any(e => e.IsDirectory && e.Name == "EmptySubFolder"), "FindEmptyFoldersAndZeroByteFilesAsync detected EmptySubFolder");
                Assert(emptyItems.Any(e => !e.IsDirectory && e.Name == "zero_byte.tmp"), "FindEmptyFoldersAndZeroByteFilesAsync detected 0-byte file");

                // 3. 親子重複選択時の解放予定サイズ排除検証
                var parentItem = new FileSystemItem { Name = "SubFolderA", FullPath = subA, IsDirectory = true, Size = 81920 };
                var childItem = new FileSystemItem { Name = "normal.log", FullPath = fileNormal, IsDirectory = false, Size = 20480 };
                long nonOverlapping = ExplorerTabViewModel.CalculateNonOverlappingBytes([parentItem, childItem]);
                Assert(nonOverlapping == 81920, $"CalculateNonOverlappingBytes deduplicated parent/child selection (Expected=81920, Actual={nonOverlapping})");

                // 4. 耐障害性完全削除検証（1ファイルを排他ロックした状態で ReadOnly+Hidden+System を含むフォルダを削除）
                using (var lockStream = new FileStream(fileLocked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    var delReport = FileDeletionService.PermanentlyDeleteItemsAsync([parentItem], scanner).GetAwaiter().GetResult();
                    Assert(!File.Exists(fileNormal), "Normal file permanently deleted");
                    Assert(!File.Exists(fileDupCopy), "Duplicate file permanently deleted");
                    Assert(!File.Exists(fileHiddenSys), "Hidden+System+ReadOnly file attributes cleared and permanently deleted");
                    Assert(File.Exists(fileLocked), "In-use locked file safely skipped without aborting sibling deletion");
                    Assert(delReport.FreedBytes == 71680, $"FreedBytes accurately reported 71,680 bytes (Actual={delReport.FreedBytes})");
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
