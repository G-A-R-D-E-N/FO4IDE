using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public class ChatHistoryService
{
    private readonly string _dir;
    // Older builds stored chats next to the exe, so each build (Debug/Release/publish) had its own
    // history that "vanished" when you ran a different one. We now use a stable per-user folder and
    // still read the legacy location so existing chats keep showing up.
    private readonly string _legacyDir;

    public ChatHistoryService()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FO4RecordEditor", "Chats");
        if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
        _legacyDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Chats");
    }

    public List<ChatSession> LoadAll()
    {
        // Merge the stable folder with every legacy per-exe folder, de-duplicating by Id. Stable
        // folder is read LAST so it wins on conflicts. Legacy dirs (Debug/Release/publish each had
        // their own) are discovered so chats saved by any old build still appear.
        var byId = new Dictionary<Guid, ChatSession>();
        var dirs = LegacyChatDirs().Append(_dir);
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try {
                    var session = JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(file));
                    if (session != null) byId[session.Id] = session;
                } catch {}
            }
        }
        return byId.Values.OrderByDescending(x => x.CreatedAt).ToList();
    }

    // Old per-exe Chats folders: the one next to the running exe, plus any sibling build-output
    // folders (bin\Debug\..\Chats, bin\Release\..\Chats) so chats saved by a different build show up.
    private IEnumerable<string> LegacyChatDirs()
    {
        yield return _legacyDir;

        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (var d = dir; d != null; d = d.Parent)
        {
            if (!string.Equals(d.Name, "bin", StringComparison.OrdinalIgnoreCase)) continue;
            string[] found;
            try { found = Directory.GetDirectories(d.FullName, "Chats", SearchOption.AllDirectories); }
            catch { yield break; }
            foreach (var c in found) yield return c;
            break;
        }
    }

    public void Save(ChatSession session)
    {
        try {
            var file = Path.Combine(_dir, $"{session.Id}.json");
            File.WriteAllText(file, JsonSerializer.Serialize(session));
        } catch {}
    }

    public void Delete(Guid id)
    {
        // Delete from the stable dir AND every legacy per-exe dir so the session doesn't
        // reappear on the next LoadAll() (which merges all dirs).
        foreach (var dir in LegacyChatDirs().Append(_dir))
        {
            try
            {
                var file = Path.Combine(dir, $"{id}.json");
                if (File.Exists(file)) File.Delete(file);
            }
            catch { }
        }
    }
}
