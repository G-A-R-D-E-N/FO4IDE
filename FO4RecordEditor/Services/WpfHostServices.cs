namespace FO4RecordEditor.Services;

/// <summary>
/// Installs the WPF implementations of the host services the shared interop layer calls through.
/// The Linux server installs its own; see FO4RecordEditor.Server/LinuxHostServices.cs.
/// </summary>
public static class WpfHostServices
{
    public static void Install()
    {
        HostServices.ShowFileDialog = Show;
        HostServices.ShowMessage = m => System.Windows.MessageBox.Show(m);
        HostServices.InvokeOnUiThread = a =>
        {
            var app = System.Windows.Application.Current;
            if (app == null) a(); else app.Dispatcher.Invoke(a);
        };
    }

    private static string Show(FileDialogRequest r)
    {
        if (r.Kind == FileDialogKind.OpenFolder)
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = r.Title };
            if (!string.IsNullOrWhiteSpace(r.InitialDirectory)) d.InitialDirectory = r.InitialDirectory;
            return d.ShowDialog() == true ? d.FolderName : "";
        }

        Microsoft.Win32.FileDialog dlg = r.Kind == FileDialogKind.SaveFile
            ? new Microsoft.Win32.SaveFileDialog()
            : new Microsoft.Win32.OpenFileDialog();
        dlg.Title = r.Title;
        dlg.Filter = r.Filter;
        if (!string.IsNullOrWhiteSpace(r.InitialDirectory)) dlg.InitialDirectory = r.InitialDirectory;
        return dlg.ShowDialog() == true ? dlg.FileName : "";
    }
}
