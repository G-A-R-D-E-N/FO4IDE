using System.Windows.Controls;
using FO4RecordEditor.Models;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Views;

public partial class CommandPalette : UserControl
{
    private readonly CommandRegistry _registry;
    public event Action? Dismiss;

    public CommandPalette(CommandRegistry registry)
    {
        InitializeComponent();
        _registry = registry;
        List.ItemsSource = registry.All;
        Box.TextChanged += (_, _) => List.ItemsSource = registry.Search(Box.Text);
        List.MouseDoubleClick += (_, _) => Run();
    }

    private void Run()
    {
        if (List.SelectedItem is AppCommand c) { c.Execute(); Dismiss?.Invoke(); }
    }
}
