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
