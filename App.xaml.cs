using System.Windows;
using System.Windows.Threading;
using WinGomiExplorer.Services;

namespace WinGomiExplorer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DiagnosticsService.LogInfo("App", $"WinGomiExplorer starting up. LogFile: {DiagnosticsService.CurrentLogPath}");

        // 1. UIスレッド未処理例外の捕捉 (Never Crash 原則)
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 2. バックグラウンド Task 未監視例外の捕捉
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 3. AppDomain レベルの致命的例外捕捉
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticsService.LogError("UIThread", "Unhandled Dispatcher Exception occurred.", e.Exception);
        e.Handled = true; // アプリの強制終了を防止
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        DiagnosticsService.LogError("TaskScheduler", "Unobserved Task Exception occurred.", e.Exception);
        e.SetObserved();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        DiagnosticsService.LogError("AppDomain", $"Unhandled Domain Exception (IsTerminating={e.IsTerminating})", e.ExceptionObject as Exception);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticsService.LogInfo("App", $"WinGomiExplorer exiting with code {e.ApplicationExitCode}.");
        base.OnExit(e);
    }
}
