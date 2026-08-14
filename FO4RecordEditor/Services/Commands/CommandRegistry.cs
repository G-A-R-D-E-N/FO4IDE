using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class CommandRegistry
{
    private readonly List<AppCommand> _commands = new();
    public IReadOnlyList<AppCommand> All => _commands;

    public void Register(AppCommand cmd) => _commands.Add(cmd);
    public void Register(string id, string title, string category, Action exec) =>
        _commands.Add(new AppCommand(id, title, category, exec));

    public IReadOnlyList<AppCommand> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _commands;
        return _commands.Where(c => FuzzyMatch(c.Title, query) || FuzzyMatch(c.Category, query))
                        .ToList();
    }

    private static bool FuzzyMatch(string text, string query)
    {
        int ti = 0, qi = 0;
        text = text.ToLowerInvariant(); query = query.ToLowerInvariant();
        while (ti < text.Length && qi < query.Length)
        {
            if (text[ti] == query[qi]) qi++;
            ti++;
        }
        return qi == query.Length;
    }
}
