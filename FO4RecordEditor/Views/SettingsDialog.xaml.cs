using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FO4RecordEditor.ViewModels;

namespace FO4RecordEditor.Views;

public partial class SettingsDialog : Window
{
    private readonly ShellViewModel _shell;

    public SettingsDialog(ShellViewModel shell)
    {
        InitializeComponent();
        _shell = shell;
        var s = shell.Settings.Current;

        ApiKeyBox.Password = s.AnthropicApiKey;
        ModelBox.Text = s.Model;
        ClaudeCodePathBox.Text = s.ClaudeCodePath;
        OllamaUrlBox.Text = s.OllamaUrl;
        OllamaModelBox.Text = s.OllamaModel;
        OutputFolderBox.Text = string.IsNullOrWhiteSpace(s.OutputFolder)
            ? FO4RecordEditor.Services.WriteService.DefaultOutputDir   // show the effective default
            : s.OutputFolder;
        DataFolderBox.Text = s.DataFolder;

        SelectProvider(s.AiProvider);
        UpdatePanels();
    }

    private void SelectProvider(string provider)
    {
        foreach (ComboBoxItem item in ProviderBox.Items)
        {
            if ((string)item.Tag == provider) { ProviderBox.SelectedItem = item; return; }
        }
        ProviderBox.SelectedIndex = 0;
    }

    private string CurrentProvider =>
        ProviderBox.SelectedItem is ComboBoxItem item ? (string)item.Tag : "anthropic";

    private void ProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePanels();

    private void UpdatePanels()
    {
        if (AnthropicPanel == null) return;   // during init before all elements exist
        var p = CurrentProvider;
        ModelPanel.Visibility      = p == "ollama"     ? Visibility.Collapsed : Visibility.Visible;
        AnthropicPanel.Visibility  = p == "anthropic"  ? Visibility.Visible : Visibility.Collapsed;
        ClaudeCodePanel.Visibility = p == "claudecode" ? Visibility.Visible : Visibility.Collapsed;
        OllamaPanel.Visibility     = p == "ollama"     ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void TestClaude_Click(object sender, RoutedEventArgs e)
    {
        ClaudeStatus.Text = "Testing...";
        var path = string.IsNullOrWhiteSpace(ClaudeCodePathBox.Text) ? "claude" : ClaudeCodePathBox.Text;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add("--version");
            var proc = Process.Start(psi)!;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            ClaudeStatus.Text = proc.ExitCode == 0
                ? $"✓ Found: {output.Trim()}"
                : "✗ `claude` ran but returned an error.";
        }
        catch (System.Exception ex)
        {
            ClaudeStatus.Text = $"✗ Not found: {ex.Message}. Install the Claude Code CLI or set the full path.";
        }
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose output folder for AI-authored plugins" };
        if (!string.IsNullOrWhiteSpace(OutputFolderBox.Text)) dlg.InitialDirectory = OutputFolderBox.Text;
        if (dlg.ShowDialog() == true) OutputFolderBox.Text = dlg.FolderName;
    }

    private void BrowseData_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the game Data folder to load plugins from" };
        if (!string.IsNullOrWhiteSpace(DataFolderBox.Text)) dlg.InitialDirectory = DataFolderBox.Text;
        if (dlg.ShowDialog() == true) DataFolderBox.Text = dlg.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = _shell.Settings.Current;
        s.AiProvider = CurrentProvider;
        s.AnthropicApiKey = ApiKeyBox.Password;
        s.Model = ModelBox.Text;
        s.ClaudeCodePath = ClaudeCodePathBox.Text;
        s.OllamaUrl = OllamaUrlBox.Text;
        s.OllamaModel = OllamaModelBox.Text;
        s.OutputFolder = OutputFolderBox.Text;
        s.DataFolder = DataFolderBox.Text.Trim();

        _shell.Settings.Save();
        _shell.RebuildProvider();
        DialogResult = true;
    }
}
