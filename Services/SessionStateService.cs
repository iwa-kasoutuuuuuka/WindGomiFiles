using System.IO;
using System.Text.Json;
using WinGomiExplorer.Models;

namespace WinGomiExplorer.Services;

public class SavedTabInfo
{
    public TabKind Kind { get; set; } = TabKind.FolderExplorer;
    public string Path { get; set; } = @"C:\";
    public bool IsPinned { get; set; }
}

public class SavedBookmarkInfo
{
    public string Title { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
}

public class AppSessionState
{
    public List<SavedTabInfo> Tabs { get; set; } = [];
    public int ActiveTabIndex { get; set; }
    public List<SavedBookmarkInfo> CustomBookmarks { get; set; } = [];
    public bool ShowVisualDashboard { get; set; } = true;
    public bool ShowDualPane { get; set; }
    public string SecondaryPanePath { get; set; } = @"C:\";
}

public static class SessionStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string GetSessionFilePath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string workspaceRoot = Path.GetFullPath(Path.Combine(baseDir, @"..\..\.."));
        string dir = Directory.Exists(workspaceRoot) && File.Exists(Path.Combine(workspaceRoot, "WinGomiExplorer.csproj"))
            ? Path.Combine(workspaceRoot, "logs")
            : Path.Combine(baseDir, "logs");

        try
        {
            Directory.CreateDirectory(dir);
        }
        catch
        {
            dir = Path.GetTempPath();
        }

        return Path.Combine(dir, "session_state.json");
    }

    public static void SaveSession(AppSessionState state)
    {
        try
        {
            string file = GetSessionFilePath();
            string json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(file, json);
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogWarning("SessionState", $"Failed to save session state: {ex.Message}");
        }
    }

    public static AppSessionState? LoadSession()
    {
        try
        {
            string file = GetSessionFilePath();
            if (!File.Exists(file)) return null;
            string json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<AppSessionState>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            DiagnosticsService.LogWarning("SessionState", $"Failed to load session state: {ex.Message}");
            return null;
        }
    }
}
