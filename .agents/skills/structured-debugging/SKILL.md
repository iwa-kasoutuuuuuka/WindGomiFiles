---
name: structured-debugging
description: >-
  WinGomiExplorer (.NET 10 / WPF / C#) に特化した体系的デバッグ・根本原因分析（RCA）スキル。
  タブ型エクスプローラー、非同期フォルダ容量計算、大容量・ゴミファイル抽出スキャン、完全削除セーフガード、
  および WPF UI スレッド・バインディングに関する不具合調査・パフォーマンス改善・回帰防止時に使用する。
---

# WinGomiExplorer 特化型・構造化デバッグガイド (Structured Debugging Guide)

`WinGomiExplorer` におけるエラーや予期しない挙動、パフォーマンス低下、表示不整合に対しては、当てずっぽうな修正（trial-and-error guessing）を厳禁とし、以下の体系的デバッグプロセスとアプリ固有の不変条件チェックリストに従って対処します。

---

## 1. 4段階の構造化デバッグプロセス

### フェーズ 1: 根本原因の特定 (Root Cause Analysis)
1. **事象とログの確認**:
   - 自動診断ログ `e:\WinGomiFiles\logs\wingomi_debug.log`（[`DiagnosticsService`](file:///e:/WinGomiFiles/Services/DiagnosticsService.cs) が出力）を確認し、未処理例外・スキャン経過時間・削除スキップ理由を特定する。
2. **レイヤーの局所化**:
   - 問題が以下のどのレイヤーで発生しているかを切り分ける：
     - **A. ファイルシステム列挙・スキャン層** ([`DiskScannerService.cs`](file:///e:/WinGomiFiles/Services/DiskScannerService.cs))
     - **B. システム保護・ゴミ分類ルール層** ([`SystemProtectionService.cs`](file:///e:/WinGomiFiles/Services/SystemProtectionService.cs))
     - **C. 完全削除・属性解除層** ([`FileDeletionService.cs`](file:///e:/WinGomiFiles/Services/FileDeletionService.cs))
     - **D. タブ状態・非同期ライフサイクル・ソート層** ([`ExplorerTabViewModel.cs`](file:///e:/WinGomiFiles/Models/ExplorerTabViewModel.cs))
     - **E. WPF XAML バインディング・仮想化 DataGrid 層** ([`MainWindow.xaml`](file:///e:/WinGomiFiles/MainWindow.xaml) / [`MainWindow.xaml.cs`](file:///e:/WinGomiFiles/MainWindow.xaml.cs))
3. **最小再現手順の確立**:
   - 特定のパス（ジャンクションを含むパス、アクセス拒否フォルダ、読み取り専用ファイル等）や特定のUI操作（高速なタブ切替、スキャン中のキャンセル等）での再現条件を特定する。

### フェーズ 2: 推論と仮説検証 (Hypothesis & Verification)
修正コードを書く前に、最低3つの仮説を立て、「悪魔の代弁者（批判的視点）」から反証・検証を行います。

### フェーズ 3: 最小限の修正と退行（デグレード）防止
- 無関係なリファクタリングを混在させず、該当レイヤーの不変条件（Invariants）を守った最小限の修正を行う。
- 修正後は必ず `dotnet build -c Release` でビルドエラー・警告が `0` であることを確認し、プロセス起動スモークテストを実施する。

---

## 2. WinGomiExplorer 固有のデバッグチェックリスト (6大不変条件)

### ① 「Never Crash」とファイルシステム例外の完全吸収
- [ ] **アクセス拒否 (`UnauthorizedAccessException`) / パス長超過 (`PathTooLongException`)**:
  - `FileSystemEnumerable<T>` では必ず `IgnoreInaccessible = true` を設定しているか？
  - 個別の `DirectoryInfo` / `FileInfo` 操作は `try-catch` で囲まれ、1つのアクセス拒否ファイルでフォルダ全体のスキャンや削除が中断しないようになっているか？
- [ ] **隠しファイル・システムファイルの常時取得**:
  - `EnumerationOptions.AttributesToSkip = 0` が維持されているか？（デフォルトの `EnumerationOptions` は Hidden と System をスキップしてしまうため注意）

### ② ジャンクション・シンボリックリンク (`ReparsePoint`) の無限ループ＆二重計上防止
- [ ] Windows の `C:\Users\All Users` や `AppData\Local\Application Data` などのジャンクション（`FileAttributes.ReparsePoint`）は、**一覧表示には含めるが、再帰的なサイズ計算および再帰削除では絶対に中へ辿らない**（`!entry.IsReparse` チェック）ようになっているか？
- [ ] ジャンクション自体を削除する際は、リンク先の中身を消さずに `dirInfo.Delete(false)` でリンクのみを削除しているか？

### ③ システム保護 (`SystemProtectionService`) と完全削除の安全性
- [ ] **ホワイトリスト優先評価の原則**:
  - `C:\Windows` 全体は保護対象だが、`C:\Windows\Temp\` や `C:\Windows\SoftwareDistribution\Download\`、`C:\Windows\Minidump\` 等の安全なゴミ領域は**先に `false`（削除可能）として判定**されているか？
- [ ] **読取専用・隠し・システム属性の解除**:
  - `File.Delete` や `Directory.Delete` を呼ぶ前に、`ReadOnly | Hidden | System` 属性が付いている場合は `FileAttributes.Normal` にリセットしているか？（リセットしないと `UnauthorizedAccessException` が発生する）
- [ ] **親子重複選択時の容量二重計上防止**:
  - 親フォルダとその内部のファイル/サブフォルダが同時にチェックされた際、`ExplorerTabViewModel.CalculateNonOverlappingBytes` によって重複バイト数が除外されているか？

### ④ WPF UI スレッド親和性 (Dispatcher) と非同期キャンセル (`CancellationToken`)
- [ ] **クロススレッド例外の防止**:
  - `Parallel.ForEachAsync` や `Task.Run` のワーカースレッド内から `DisplayedItems` (`ObservableCollection<T>`) を直接操作していないか？ コレクション変更は必ずUIスレッド（または `Dispatcher.InvokeAsync`）で行っているか？
- [ ] **ナビゲーション競合（レースコンディション）の防止**:
  - ユーザーが別フォルダへ連続移動した際やタブを閉じた際、`CancelCurrentWork()` で古い `CancellationTokenSource` をキャンセルし、古いタスクの計算結果が新しいフォルダの表示を上書きしないよう `token.IsCancellationRequested` を確認しているか？
- [ ] **UI 更新スロットリング**:
  - 数十万ファイルのスキャン中やサブフォルダ計算中に、1ファイルごとに `Dispatcher.InvokeAsync` や再ソートを呼んでUIメッセージキューを溢れさせていないか？（`Environment.TickCount64` で 120ms〜350ms 間隔にスロットリングすること）

### ⑤ 容量順ソートと視覚的容量バー (`SizeRatio`) の整合性
- [ ] **文字列ソート誤爆の防止**:
  - `DataGrid` のサイズ列のソートが `"100 KB"` vs `"2 GB"` の辞書順ソートにならないよう、`ItemsDataGrid_Sorting` で `e.Handled = true` とし、`SortableSize` (`long` 型の実バイト数) でソートしているか？
- [ ] **相対容量バー (`SizeRatio`) のゼロ除算・NaN 防止**:
  - `maxSize == 0` や未計算（`Size < 0`）のときに `0.0` にクランプされ、`NaN` や `Infinity` が WPF の `Width` バインディングに渡らないようになっているか？（`Width` に `NaN` 以外で負数や `Infinity` が渡ると WPF レンダリング例外になる）

### ⑥ ビルド・起動スモーク検証コマンド
コード変更後は必ず以下の手順で検証を行うこと：
```powershell
# 1. Release ビルドでエラー・警告 0 を確認
dotnet build -c Release

# 2. 起動スモークテスト（起動直後の XAML パース例外や DI/初期化クラッシュがないか検証）
$p = Start-Process -FilePath "e:\WinGomiFiles\bin\Release\net10.0-windows\WinGomiExplorer.exe" -PassThru
Start-Sleep -Seconds 3
if ($p.HasExited) { Write-Error "Crash detected! ExitCode: $($p.ExitCode)" } else { Stop-Process -Id $p.Id -Force }

# 3. 診断ログの確認
Get-Content "e:\WinGomiFiles\logs\wingomi_debug.log" -Tail 30
```
