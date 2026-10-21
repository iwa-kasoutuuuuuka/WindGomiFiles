<p align="center">
  <img src="Assets/app_icon.png" width="140" height="140" alt="WinGomiExplorer Icon" />
</p>

<h1 align="center">WinGomiExplorer (Pro Edition)</h1>

<p align="center">
  <strong>Windows 11 向け 容量順ソート対応・タブ型ファイルエクスプローラー ＆ 大容量ゴミファイル抽出スキャナー</strong>
</p>

`WinGomiExplorer` は、Windows 標準のファイルエクスプローラーでは手が届かない**「フォルダごとの容量表示・サイズ順ソート」「ツリーマップ視覚化」「拡張子・隠しファイル・システムファイルの常時全表示」「重複ファイル検出」「開発キャッシュ・空フォルダ整理」「2画面分割」「Spaceキー高速プレビュー」**を1つに統合した、C# / .NET 10 (WPF) 製のネイティブデスクトップアプリケーションです。

---

## ✨ 主な特徴・全実装機能

### 1. 📊 フォルダ容量の非同期高速計算 ＆ 容量順ソート
* 従来のファイルエクスプローラーでは空欄だった**「フォルダのサイズ」**を、バックグラウンド並列処理（`FileSystemEnumerable` + `Parallel.ForEachAsync`）で高速に自動集計します。
* フォルダとファイルを合わせた**「容量の大きい順（降順）」ソート**に標準対応し、視覚的な**容量占有バー**でどのフォルダ・ファイルがディスクを圧迫しているか一目で把握できます。

### 2. 🗺️ インタラクティブ・ツリーマップ（Treemap）＆ 拡張子別シェア統計
* **ツリーマップ面積グラフ**: WinDirStat / SpaceMonger 風に、ファイルやフォルダの容量占有率を面積比例のカラーブロックでリアルタイム描画します（クリックで該当行を選択、ダブルクリックで開く）。
* **拡張子・種別シェア Top 8**: どの拡張子（`.vhdx`, `.mp4`, `.iso`, `.log`, `.tmp` 等）が容量の何％を占めているかを自動集計し、ワンクリックでその拡張子のみに絞り込めます。

### 3. 🔍 大容量・ゴミファイル・重複ファイル抽出スキャン（4モード切替）
スキャン完了後、**再スキャン不要（0秒）**で以下のモードと最小サイズしきい値（`10MB+` 〜 `1GB+`）を切り替えられます：
1. **📄 巨大ファイル Topランキング**: 指定サイズ以上の巨大ファイルを大きい順に一覧表示。
2. **📁 巨大フォルダ Topランキング**: 肥大化しているディレクトリを大きい順に一覧表示。
3. **🧹 ゴミ・一時ファイル・キャッシュ候補**: `Temp`、ブラウザ/GPUキャッシュ、`CrashDumps`、開発キャッシュ等を自動抽出。
4. **🧬 重複ファイル検出 (Duplicate Finder)**: 同一サイズ＆高速SHA-256フィンガープリント照合により、重複して放置されている同一ファイルを検出。原本を残して**「重複コピー側のみ」を自動チェック**します。

### 4. 🧰 スマートクリーン ＆ 開発キャッシュ・空フォルダ・WinSxS診断 (`SmartCleanerWindow`)
* **🔧 開発ツール＆システムキャッシュ一括診断**: `NuGet`, `npm`, `pip`, `uv`, `Cargo`, `Gradle`, `Maven`, `Docker/WSL (vhdx)`, `Chrome/Edge Cache`, `DirectX ShaderCache`, `Windows Update Download` 等の使用容量を一括計測・クリーンアップ。
* **📂 空フォルダ・0バイトゴミファイル一括検出**: アンインストール残骸などの空ディレクトリや0バイトファイルを高速抽出。
* **🛡️ Windows コンポーネントストア (WinSxS) 診断レポート**: `Windows.old`、`DeliveryOptimization`、`WER`、`WinSxS` の状態診断と最適化コマンドガイドを表示。

### 5. 👁️ Spaceキーで即座に中身を確認する「クイックプレビュー (Quick Look)」
* ファイルやフォルダを選択して **`Space` キー**を押すだけで、外部アプリを起動せず瞬時に中身をポップアップ表示します：
  * **テキスト・ソースコード・巨大ログ**: 数GBのログファイルでも先頭 128 KB をストリーム読込し **0.01秒** でプレビュー。
  * **画像プレビュー**: `.png`, `.jpg`, `.gif`, `.bmp`, `.ico`, `.webp` 等を高画質表示＋解像度表示。
  * **バイナリ Hex ダンプ**: `.exe`, `.dll`, `.iso`, `.dmp` 等の先頭 1,024 バイトを 16進数 Hex + ASCII ビューで表示し、ワンクリックで SHA-256 ハッシュも計算可能。

### 6. 🪟 2画面分割（デュアルペイン / Split View）＆ ✏️ 高度な一括リネーム
* **2画面分割表示**: 左右に2つのエクスプローラー画面を並べて表示し、`➡ コピー` / `➡ 移動` / `⬅ コピー` / `⬅ 移動` ボタンでフォルダ間の整理が快適に行えます。
* **一括リネーム (`F2` キー)**: 検索＆置換（**正規表現 Regex 対応**）、接頭辞・接尾辞、更新日付（`yyyyMMdd_`）、連番（`_001`, `_002`...）をリアルタイムプレビュー付きで一括適用。
* **📌 タブのピン留め＆セッション自動復元**: タブのピン留め（`📌`）や左サイドバーへの「⭐お気に入り追加」に対応し、次回起動時に前回のタブ状態を自動復元します。

### 7. 🛡️ システム保護ロック ＆ 耐障害性「完全削除」エンジン
* `C:\Windows\System32`、`WinSxS`、`explorer.exe`、`pagefile.sys`、`ntuser.dat`、および標準シェルフォルダの器自体は `🛡️ システム保護` として自動ロックされます。
* 実行可能ファイル（`.exe`, `.bat`, `.ps1` 等）の誤ダブルクリック実行を防ぐセキュリティ確認ガードを搭載。

---

## ⌨️ キーボードショートカット一覧

| 操作 | ショートカット / アクション |
| :--- | :--- |
| **クイックプレビューを開く / 閉じる** | `Space` キー |
| **選択項目を一括リネーム** | `F2` キー |
| **新しいフォルダタブを開く** | `Ctrl + T` |
| **現在のタブを閉じる** | `Ctrl + W`（ピン留めタブは保護確認あり） |
| **戻る / 進む / 上の階層へ** | `Alt + ←` / `Alt + →` / `Alt + ↑` |
| **最新の情報に更新・容量再計算** | `F5` |
| **選択項目を完全削除** | `Delete` |

---

## 🚀 ビルド・起動・自動テスト方法

```powershell
# 1. リリースビルド
dotnet build -c Release

# 2. アプリケーションの起動
Start-Process ".\bin\Release\net10.0-windows\WinGomiExplorer.exe"

# 3. 内蔵の自動動作検証・セキュリティテストスイート (全24項目) の実行
Start-Process ".\bin\Release\net10.0-windows\WinGomiExplorer.exe" -ArgumentList "--self-test" -Wait
```

---

## 🏗️ プロジェクト構成

```text
e:\WinGomiFiles\
├── Assets\
│   ├── app_icon.png                     # アプリアイコン画像 (512x512 透過PNG)
│   └── app_icon.ico                     # 実行ファイル・ウィンドウ用マルチサイズICO
├── Controls\
│   └── TreemapControl.cs                # インタラクティブ容量ツリーマップ描画コントロール
├── Views\
│   ├── DeleteConfirmWindow.xaml(.cs)    # 完全削除確認モーダルダイアログ
│   ├── QuickPreviewWindow.xaml(.cs)     # Spaceキー高速プレビュー (テキスト/画像/Hex/SHA256)
│   ├── BatchRenameWindow.xaml(.cs)      # 正規表現・連番・日付対応 一括リネームウィンドウ
│   └── SmartCleanerWindow.xaml(.cs)     # 開発キャッシュ・空フォルダ・WinSxS診断クリーナー
├── Models\
│   ├── FileSystemItem.cs                # ファイル/フォルダ項目モデル
│   ├── DriveItem.cs                     # ドライブ使用率 & クイックアクセスモデル
│   └── ExplorerTabViewModel.cs          # タブ別ViewModel (ソート/スキャン/重複/拡張子統計/ピン留め)
├── Services\
│   ├── DiskScannerService.cs            # 高速ディレクトリ列挙・並列容量計算・大容量スキャン
│   ├── DuplicateDetectorService.cs      # 高速重複ファイル検出・空フォルダ検出・開発キャッシュ計測
│   ├── SystemProtectionService.cs       # システム重要領域保護 & ゴミ自動分類ルール
│   ├── FileDeletionService.cs           # 属性解除・ロック耐性付き完全削除エンジン
│   ├── SessionStateService.cs           # タブ・ピン留め・お気に入り状態のJSON永続化サービス
│   ├── DiagnosticsService.cs            # 構造化デバッグロガー (logs/wingomi_debug.log)
│   └── SelfTestRunner.cs                # 自動動作検証・セキュリティ回帰テストランナー (--self-test)
├── GEMINI.md                            # AIペアプログラミング用 思考プロセス・特化型デバッグ指針
└── .agents\skills\structured-debugging\ # WinGomiExplorer特化型デバッグスキル定義
```
