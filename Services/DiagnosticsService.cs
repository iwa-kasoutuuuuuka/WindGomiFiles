using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinGomiExplorer.Services;

/// <summary>
/// WinGomiExplorer 専用の構造化デバッグ・診断ロガー。
/// 非同期スキャン速度、アクセス拒否パス、削除オペレーション、および未処理例外を
/// スレッドセーフに logs/wingomi_debug.log へ記録します。
/// </summary>
public static class DiagnosticsService
{
    private static readonly Lock SyncLock = new();
    private static readonly string LogFilePath;

    static DiagnosticsService()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // ワークスペース直下に logs フォルダがあれば優先、なければ実行ファイル配下
        string workspaceRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\.."));
        string logDir = Directory.Exists(workspaceRoot) && File.Exists(Path.Combine(workspaceRoot, "WinGomiExplorer.csproj"))
            ? Path.Combine(workspaceRoot, "logs")
            : Path.Combine(baseDir, "logs");

        try
        {
            Directory.CreateDirectory(logDir);
        }
        catch
        {
            logDir = Path.GetTempPath();
        }

        LogFilePath = Path.Combine(logDir, "wingomi_debug.log");
    }

    public static string CurrentLogPath => LogFilePath;

    public static void LogInfo(string module, string message)
        => WriteEntry("INFO", module, message, null);

    public static void LogWarning(string module, string message)
        => WriteEntry("WARN", module, message, null);

    public static void LogError(string module, string message, Exception? ex = null)
        => WriteEntry("ERROR", module, message, ex);

    private static void WriteEntry(string level, string module, string message, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [T:{Environment.CurrentManagedThreadId}] [{module}] {message}");
            if (ex is not null)
            {
                sb.AppendLine();
                sb.Append($"  Exception: {ex.GetType().FullName}: {ex.Message}");
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                {
                    sb.AppendLine();
                    sb.Append($"  StackTrace: {ex.StackTrace}");
                }
            }
            sb.AppendLine();

            string line = sb.ToString();
            Debug.Write(line);

            lock (SyncLock)
            {
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // ロガー自体のI/O例外でアプリを止めない（Never Crash原則）
        }
    }

    public static IDisposable BeginTimedScope(string module, string operation)
        => new TimedScope(module, operation);

    private sealed class TimedScope : IDisposable
    {
        private readonly string _module;
        private readonly string _operation;
        private readonly Stopwatch _sw;

        public TimedScope(string module, string operation)
        {
            _module = module;
            _operation = operation;
            _sw = Stopwatch.StartNew();
            LogInfo(_module, $"START: {_operation}");
        }

        public void Dispose()
        {
            _sw.Stop();
            LogInfo(_module, $"END: {_operation} (Elapsed: {_sw.ElapsedMilliseconds} ms)");
        }
    }
}
