using FO4RecordEditor.Models;
using Mutagen.Bethesda.Plugins;

namespace FO4RecordEditor.Services;





public static class ConflictState
{
    private static Dictionary<FormKey, string> _winners = new();
    private static Dictionary<string, ConflictStatus> _pluginStatus = new(StringComparer.OrdinalIgnoreCase);

    public static bool HasData => _winners.Count > 0;

    public static void Set(IEnumerable<ConflictEntry> conflicts)
    {
        var d = new Dictionary<FormKey, string>();
        var ps = new Dictionary<string, ConflictStatus>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in conflicts)
        {
            if (FormKey.TryFactory(c.FormKey, out var fk)) d[fk] = c.Winner;


            foreach (var p in c.Plugins)
            {
                var st = string.Equals(p, c.Winner, StringComparison.OrdinalIgnoreCase)
                    ? ConflictStatus.ConflictWinner : ConflictStatus.ConflictLoser;
                if (!ps.TryGetValue(p, out var cur)) ps[p] = st;
                else if (st == ConflictStatus.ConflictLoser) ps[p] = ConflictStatus.ConflictLoser;
            }
        }

        _winners = d;
        _pluginStatus = ps;
    }

    public static ConflictStatus GetPluginStatus(string plugin) =>
        _pluginStatus.TryGetValue(plugin, out var s) ? s : ConflictStatus.None;

    public static void Clear() { _winners = new(); _pluginStatus = new(StringComparer.OrdinalIgnoreCase); }

    public static ConflictStatus GetStatus(string plugin, FormKey fk)
    {
        if (!_winners.TryGetValue(fk, out var winner)) return ConflictStatus.None;
        return string.Equals(plugin, winner, StringComparison.OrdinalIgnoreCase)
            ? ConflictStatus.ConflictWinner
            : ConflictStatus.ConflictLoser;
    }

    public static ConflictStatus GetStatus(string plugin, string formKeyStr) =>
        FormKey.TryFactory(formKeyStr, out var fk) ? GetStatus(plugin, fk) : ConflictStatus.None;
}
