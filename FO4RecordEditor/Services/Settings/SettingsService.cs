using System.IO;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

public sealed class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "FO4RecordEditor");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public AppSettings Current { get; private set; } = new();

    private static readonly Dictionary<string, string> _retiredModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-3-7-sonnet-20250219"] = "claude-sonnet-4-6",
        ["claude-3-5-sonnet-20241022"] = "claude-sonnet-4-6",
        ["claude-3-5-sonnet-20240620"] = "claude-sonnet-4-6",
        ["claude-3-sonnet-20240229"]   = "claude-sonnet-4-6",
        ["claude-3-5-haiku-20241022"]  = "claude-haiku-4-5",
        ["claude-3-opus-20240229"]     = "claude-opus-4-8",
        ["claude-2.1"]                 = "claude-sonnet-4-6",
        ["claude-2.0"]                 = "claude-sonnet-4-6",
    };

    internal static string MigrateModel(string? model) =>
        model != null && _retiredModels.TryGetValue(model, out var replacement) ? replacement : (model ?? "");

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(FilePath))
                          ?? new AppSettings();
        }
        catch { Current = new AppSettings(); }

        Current.Model = MigrateModel(Current.Model);
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonConvert.SerializeObject(Current, Formatting.Indented));
    }
}
