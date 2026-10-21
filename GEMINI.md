# 思考プロセスに関する指示

回答を出力する前に、バックグラウンドで必ず以下のステップを踏んで思考してください。
1. ユーザーの意図と背景にある本質的な課題の抽出
2. 複数の異なるアプローチや仮説のリストアップ（最低3つ）
3. 各仮説のメリット・デメリット、論理的矛盾の自己検証
4. 最も合理的かつ網羅的な結論の導出
5. すぐに結論を出さず、段階的にステップに分けて論理的に検証した上で回答してください
6. あえて批判的な視点（悪魔の代弁者）からも検証し、最もハルシネーション（嘘）の少ない確実な解決策を提示してください。

---

# デバッグに関する指示とアプローチ (Debugging Guidelines)

Anti-Gravity 環境におけるエラーの発生や予期しない挙動に対しては、当てずっぽうな修正を避け、以下の**構造化されたデバッグプロセス**に従って思考・対処してください。

## 1. 根本原因の特定 (Root Cause Analysis)
単に「動かない」という現象に対応するのではなく、エラーの本質を突き止めます。
* **事象の明確化**: 何が起きているか（エラーログ、期待値と実際の出力の乖離）を正確に言語化する。
* **エラーの局所化**: 影響を受けているモジュール、関数、またはコンポーネントを特定し、問題を最小限のコード片に切り離す。
* **再現手順の確立**: そのバグを100%再現するための最小限の手順（ミニマルコード、特定の入力データ）を特定する。

## 2. 推論と仮説検証 (Hypothesis & Verification)
修正コードを書き始める前に、必ず「なぜそのエラーが起きているのか」の仮説を立てます。
* **仮説の構築**: 「〇〇のタイミングでデータがNullになっているのではないか」「Anti-Gravity特有のライフサイクルによるものではないか」といった仮説を複数立てる。
* **検証の実行**: ログの出力（`DiagnosticsService.LogInfo/LogError` / `Debug.Write` 等）、または状態のモックを行い、立てた仮説が正しいかどうかを1つずつ検証する。
* **推論プロセスの記録**: 複雑なバグの場合、AIにプロンプトを投げる際、またはコードコメントに残すために「何を試し、何が違ったか」のログを頭の中（またはチャット）に整理する。

## 3. 修正の適用と退行（デグレード）防止
* **最小限の修正**: バグを直すためだけの最もシンプルでクリーンな修正を心がけ、ついでに他の無関係なリファクタリングを同時に行わない。
* **副作用の考慮**: その修正が、他のコンポーネントや依存関係に影響を与えないかをレビューする。
* **再発防止策**: 修正が完了したら、同様のバグが二度と発生しないようにするための検証・ガード設計（アサーションや境界条件チェック）を追加する。

## 4. AI (Gemini) へのデバッグ依頼時のプロンプト作法
AIにデバッグの支援を求める際は、以下の情報を必ず提示して思考を同期させてください。
1. **コンテキスト**: 実行しようとしていることの目的と、現在のコード全体（または関連する関数）。
2. **エラーの詳細**: スタックトレース、エラーメッセージの全文、または期待される挙動と実際の挙動。
3. **試したこと**: すでに検証した仮説や、試して効果がなかったアプローチ（AIが同じ提案を繰り返すのを防ぐため）。

---

# WinGomiExplorer 特化型デバッグ＆不変条件ガイドライン

本プロジェクト（`WinGomiExplorer` - C# / .NET 10 / WPF）のデバッグおよび機能改修時は、常に以下のアーキテクチャ境界と不変条件（Invariants）を検証してください。

## 1. 診断ログとクラッシュガードの確認 (`Services/DiagnosticsService.cs` & `App.xaml.cs`)
- アプリ実行時のすべての未処理例外（`DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException`, `AppDomain.UnhandledException`）およびスキャン・削除トレースは `e:\WinGomiFiles\logs\wingomi_debug.log` に自動出力されます。
- デバッグ開始時はまず `e:\WinGomiFiles\logs\wingomi_debug.log` を確認し、スレッドID・経過時間・例外スタックトレースを特定してください。

## 2. モジュール別の重点検証ポイント

### A. 高速ディレクトリスキャンとフォルダ容量計算 (`Services/DiskScannerService.cs`)
1. **隠しファイル・システムファイル全表示の保証**:
   - `EnumerationOptions.AttributesToSkip = 0` および `IgnoreInaccessible = true` を常に維持すること。
2. **ジャンクション・シンボリックリンク (`FileAttributes.ReparsePoint`) のループ防止**:
   - `C:\Users\All Users` 等のジャンクションはフォルダ一覧には表示するが、**再帰的なサイズ集計・フルスキャン時は絶対に内部を辿らない**（無限ループおよび容量の二重計上防止）。
3. **キャッシュ整合性 (`_folderSizeCache`)**:
   - ファイルやフォルダを削除した際、または手動更新（`F5` / `forceRefreshSizes: true`）時は、必ず `InvalidatePathAndAncestors(path)` で対象パスと親階層すべてのキャッシュを無効化すること。

### B. システム保護と完全削除エンジン (`Services/SystemProtectionService.cs` & `Services/FileDeletionService.cs`)
1. **システム保護の優先順位（ホワイトリスト優先）**:
   - `C:\Windows` 配下は原則保護対象だが、`C:\Windows\Temp\` や `C:\Windows\SoftwareDistribution\Download\`、`C:\Windows\Minidump\` 等の安全な削除対象は、システム保護判定より**先に `false`（削除許可）として返す**こと。
2. **ロック中ファイルに対する耐障害性 (`DeleteDirectoryResilient`)**:
   - フォルダ削除時に `Directory.Delete(path, true)` を一括呼び出しすると、1つの使用中ファイルで全体が止まるため使用禁止。必ずファイル単位で `ReadOnly | Hidden | System` 属性を `FileAttributes.Normal` に解除して個別削除し、ロック中のファイルだけスキップして残りを完全削除すること。
3. **親子重複選択時のサイズ計算 (`CalculateNonOverlappingBytes`)**:
   - スキャン結果で親フォルダとその子孫（サブフォルダ・ファイル）が同時にチェックされた場合、解放予定容量が二重計上されないよう重複排除ロジックを通すこと。

### C. マルチタブ・非同期UI更新・DataGridソート (`Models/ExplorerTabViewModel.cs` & `MainWindow.xaml`)
1. **UIスレッド（Dispatcher）境界とスロットリング**:
   - バックグラウンドスレッド（`Parallel.ForEachAsync` / `Task.Run`）から `DisplayedItems` (`ObservableCollection`) を直接変更しないこと。
   - 進捗通知やフォルダサイズ反映による再ソートは `120ms〜350ms` 間隔でスロットリングし、UIスレッドのメッセージポンプ飽和（フリーズ）を防ぐこと。
2. **非同期タスクのキャンセル競合防止**:
   - フォルダ移動・再スキャン・タブクローズ時は必ず `CancelCurrentWork()` を呼び、ワーカースレッド側では `ct.IsCancellationRequested` を確認してからUIへ反映すること。
3. **数値ソートの保証 (`SortableSize`)**:
   - サイズ列のソートは表示文字列（`"100 KB"` 等）ではなく、必ず `SortableSize` (`long` 型の実バイト数) を基準に行うこと。

## 3. 修正後の必須検証コマンド
コードを修正した際は、完了報告前に必ず以下のビルド＆スモークテストを実行してください。
```powershell
dotnet build -c Release
$p = Start-Process -FilePath "e:\WinGomiFiles\bin\Release\net10.0-windows\WinGomiExplorer.exe" -PassThru; Start-Sleep -Seconds 3; if ($p.HasExited) { Write-Error "Exited with $($p.ExitCode)"; exit 1 } else { Stop-Process -Id $p.Id -Force }
Get-Content "e:\WinGomiFiles\logs\wingomi_debug.log" -Tail 20
```
