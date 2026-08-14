using System.Collections;
using System.Text.Json;

namespace FO4RecordEditor.Services;







public static partial class MutagenLoader
{







    public static string GetRecordsGridJson(object? envObj, string plugin, string sig, int limit, int offset = 0)
    {
        var mod = ResolveMod(plugin, envObj);
        if (mod == null) return JsonSerializer.Serialize(new { error = $"Plugin '{plugin}' not found." });
        var idx = GetModIndex(mod, plugin);
        var recs = RecordsOfSig(idx, sig);
        if (recs.Count == 0)
            return JsonSerializer.Serialize(new { columns = Array.Empty<string>(), rows = Array.Empty<object>(), total = 0, offset });

        var take = recs.Skip(Math.Max(0, offset)).Take(Math.Max(1, limit)).ToList();
        if (take.Count == 0)
            return JsonSerializer.Serialize(new { columns = Array.Empty<string>(), rows = Array.Empty<object>(), total = recs.Count, offset });

        var colProps = DiscoverGridColumns(take[0], 12);
        var columns = colProps.Select(p => p.Name).ToArray();

        var rows = take.Select(rec =>
        {
            var cells = colProps.Select(prop =>
            {
                try
                {
                    var v = prop.GetValue(rec);
                    return v switch
                    {
                        null => "",
                        Mutagen.Bethesda.Plugins.IFormLinkIdentifier fli => FormatFormLink(fli),
                        IEnumerable ie when v.GetType() != typeof(string) => SummarizeEnumerable(ie),
                        _ => v.ToString() ?? "",
                    };
                }
                catch { return "?"; }
            }).ToArray();
            return new { formKey = rec.FormKey.ToString(), editorId = rec.EditorID ?? "", cells };
        }).ToArray();

        return JsonSerializer.Serialize(new { columns, rows, total = recs.Count, offset });
    }






    private static string SummarizeEnumerable(IEnumerable ie)
    {
        const int show = 3;
        var parts = new List<string>();
        int total = 0, rendered = 0;
        foreach (var item in ie)
        {
            total++;
            if (rendered >= show) continue;
            try
            {
                string text;
                if (item == null) continue;
                if (item is Mutagen.Bethesda.Plugins.IFormLinkIdentifier fli) text = FormatFormLink(fli);
                else
                {
                    text = item.ToString() ?? "";


                    var t = item.GetType();
                    if (text.Length == 0 || text == t.FullName || text == t.ToString()) continue;
                }
                if (text.Length == 0) continue;
                parts.Add(text);
                rendered++;
            }
            catch {  }
        }
        if (total == 0) return "";
        if (parts.Count == 0) return total == 1 ? "[1 item]" : $"[{total} items]";
        var head = string.Join(", ", parts);
        return total > parts.Count ? $"{head}, +{total - parts.Count} more" : head;
    }
}
