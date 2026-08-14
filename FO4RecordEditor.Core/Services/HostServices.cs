using System;

namespace FO4RecordEditor.Services;

public enum FileDialogKind { OpenFile, OpenFolder, SaveFile }

public sealed class FileDialogRequest
{
    public FileDialogKind Kind { get; init; }
    public string Title { get; init; } = "";
    public string Filter { get; init; } = "";
    public string InitialDirectory { get; init; } = "";
}

public static class HostServices
{

    public static Func<FileDialogRequest, string> ShowFileDialog { get; set; } = _ => "";

    public static Action<string> ShowMessage { get; set; } = _ => { };

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
