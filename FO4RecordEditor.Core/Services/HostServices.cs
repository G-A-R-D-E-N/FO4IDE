using System;

namespace FO4RecordEditor.Services;

/// <summary>What kind of picker a host should show.</summary>
public enum FileDialogKind { OpenFile, OpenFolder, SaveFile }

/// <summary>A picker request. Filter uses the Win32 "Label|*.ext" form; hosts that do not speak
/// that form parse it.</summary>
public sealed class FileDialogRequest
{
    public FileDialogKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Filter { get; init; } = "";
    public string InitialDirectory { get; init; } = "";
}

/// <summary>
/// Host-supplied services the shared interop layer needs but cannot implement itself.
///
/// The interop classes are compiled into both the WPF shell and the cross-platform server, so they
/// must not reference a UI toolkit directly. Each host installs its own implementations at startup;
/// the defaults are inert so an unhosted process (tests, the headless MCP server) still runs.
/// </summary>
public static class HostServices
{
    /// <summary>Show a native file/folder picker. Returns the chosen path, or "" if cancelled.</summary>
    public static Func<FileDialogRequest, string> ShowFileDialog { get; set; } = _ => "";

    /// <summary>Show a plain informational message to the user.</summary>
    public static Action<string> ShowMessage { get; set; } = _ => { };

    /// <summary>
    /// Run an action on whatever thread owns the shell's observable collections. WPF binds
    /// directly to those, so mutating them off the dispatcher throws; the server has no such
    /// affinity and runs inline under a lock.
    /// </summary>
    public static Action<Action> InvokeOnUiThread { get; set; } = a => a();

    public static string PickFile(string title, string filter, string initialDir = "") =>
        ShowFileDialog(new FileDialogRequest
        {
            Kind = FileDialogKind.OpenFile,
            Title = string.IsNullOrWhiteSpace(title) ? "Select a file" : title,
            Filter = string.IsNullOrWhiteSpace(filter) ? "All files|*.*" : filter,
            InitialDirectory = initialDir,
        });

    public static string PickFolder(string title, string initialDir = "") =>
        ShowFileDialog(new FileDialogRequest
        {
            Kind = FileDialogKind.OpenFolder,
            Title = string.IsNullOrWhiteSpace(title) ? "Select a folder" : title,
            InitialDirectory = initialDir,
        });

    public static string PickSavePath(string title, string filter, string initialDir = "") =>
        ShowFileDialog(new FileDialogRequest
        {
            Kind = FileDialogKind.SaveFile,
            Title = string.IsNullOrWhiteSpace(title) ? "Save as" : title,
            Filter = string.IsNullOrWhiteSpace(filter) ? "All files|*.*" : filter,
            InitialDirectory = initialDir,
        });
}
