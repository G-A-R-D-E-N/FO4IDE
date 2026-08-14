using System.IO;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

public static class ToolPaths
{
    private static AppSettings? _settings;

    private static AppSettings Settings
    {
        get
        {
            if (_settings != null) return _settings;
            try
            {
                var file = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FO4RecordEditor", "settings.json");
                _settings = File.Exists(file)
                    ? JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(file)) ?? new AppSettings()
                    : new AppSettings();
            }
            catch { _settings = new AppSettings(); }
            return _settings;
        }
    }

    public static void Invalidate() => _settings = null;

    public static bool ReadLargePluginsIntoMemory
    {
        get
        {
            var env = System.Environment.GetEnvironmentVariable("FO4RE_FULL_PLUGIN_READS");
            if (!string.IsNullOrWhiteSpace(env))
                return env == "1" || env.Equals("true", System.StringComparison.OrdinalIgnoreCase);
            return Settings.ReadLargePluginsIntoMemory;
        }
    }

    private static string App(params string[] parts) =>
        Path.Combine(new[] { AppContext.BaseDirectory }.Concat(parts).ToArray());

    private static string? FirstFile(params string?[] candidates) =>
        candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p));

    private static string? FirstDir(params string?[] candidates) =>
        candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p));

    private static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim().Trim('"');
    }

    private static string? WhichOnPath(params string[] exeNames)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;
        foreach (var dir in pathVar.Split(Path.PathSeparator, System.StringSplitOptions.RemoveEmptyEntries))
        {
            var d = dir.Trim().Trim('"');
            if (d.Length == 0) continue;
            foreach (var name in exeNames)
            {
                try
                {
                    var candidate = Path.Combine(d, name);
                    if (File.Exists(candidate)) return candidate;
                }
                catch {  }
            }
        }
        return null;
    }

    public static string? Fallout4Root()
    {
        var s = Settings.Fallout4Path;
        if (!string.IsNullOrWhiteSpace(s) && Directory.Exists(s)) return s;

        foreach (var key in new[]
                 {
                     @"SOFTWARE\WOW6432Node\Bethesda Softworks\Fallout4",
                     @"SOFTWARE\Bethesda Softworks\Fallout4",
                 })
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(key);
                var path = k?.GetValue("installed path") as string;
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) return path;
            }
            catch {  }
        }

        var relative = new[]
        {
            new[] { "SteamLibrary", "steamapps", "common", "Fallout 4" },
            new[] { "Program Files (x86)", "Steam", "steamapps", "common", "Fallout 4" },
            new[] { ".steam", "steam", "steamapps", "common", "Fallout 4" },
            new[] { ".local", "share", "Steam", "steamapps", "common", "Fallout 4" },
        };

        var bases = new List<string>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives()) bases.Add(drive.Name);
        }
        catch {  }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home)) bases.Add(home);

        foreach (var root in bases)
        {
            foreach (var rel in relative)
            {
                try
                {
                    var probe = Path.Combine(new[] { root }.Concat(rel).ToArray());
                    if (Directory.Exists(probe)) return probe;
                }
                catch {  }
            }
        }
        return null;
    }

    public static string? Niftool() => FirstFile(
        Env("NIFTOOL_PATH"),
        Settings.NiftoolPath,
        App("tools", "niftool", "niftool.exe"),
        App("niftool", "niftool.exe"),
        WhichOnPath("niftool.exe"),
        @"E:\F4SE OG\Tools\PluginEditTool\tools\niftool\build\windows\x64\release\niftool.exe");

    public static string? Texconv() => FirstFile(
        Env("TEXCONV_PATH"),
        Settings.TexconvPath,
        App("tools", "texconv", "Texconvx64.exe"),
        App("tools", "texconv", "Texconv.exe"),
        WhichOnPath("Texconvx64.exe", "Texconv.exe"),
        @"E:\F4SE OG\Tools\PluginEditTool\FO4RecordEditor\TES5Edit-dev-4.1.6\Build\Edit Scripts\Texconvx64.exe",
        @"E:\F4SE OG\Tools\PluginEditTool\FO4RecordEditor\TES5Edit-dev-4.1.6\Build\Edit Scripts\Texconv.exe");

    public static string? PapyrusCompiler()
    {
        var root = Fallout4Root();
        return FirstFile(
            Env("PAPYRUS_COMPILER_PATH"),
            Settings.PapyrusCompilerPath,
            root == null ? null : Path.Combine(root, "Papyrus Compiler", "PapyrusCompiler.exe"),
            @"E:\SteamLibrary\steamapps\common\Fallout 4 1946160\Papyrus Compiler\PapyrusCompiler.exe",
            @"E:\F4SE OG\Tools\PapyrusCompiler\Papyrus Compiler\PapyrusCompiler.exe");
    }

    public static string? Archive2()
    {
        var root = Fallout4Root();
        return FirstFile(
            Env("ARCHIVE2_PATH"),
            Settings.Archive2Path,
            @"E:\F4SE OG\Tools\KnownModTools\BA2Packer\Archive2.exe",
            root == null ? null : Path.Combine(root, "Tools", "Archive2", "Archive2.exe"),
            @"E:\SteamLibrary\steamapps\common\Fallout 4 1946160\Tools\Archive2\Archive2.exe");
    }

    public static List<string> PapyrusBaseImports()
    {
        var roots = new List<string>();

        void Add(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try
            {
                dir = dir.Trim().Trim('"');
                if (Directory.Exists(dir) && !roots.Contains(dir, StringComparer.OrdinalIgnoreCase))
                    roots.Add(dir);
            }
            catch {  }
        }

        foreach (var p in (Env("PAPYRUS_BASE_IMPORTS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            Add(p);
        foreach (var p in (Settings.PapyrusBaseImports ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            Add(p);

        Add(App("tools", "papyrus", "F4SE"));
        Add(App("tools", "papyrus", "Base"));

        var root = Fallout4Root();
        if (root != null)
        {
            Add(Path.Combine(root, "Data", "Scripts", "Source", "User"));
            Add(Path.Combine(root, "Data", "Scripts", "Source", "Base"));
        }

        Add(@"E:\F4SE OG\Tools\f4se_0_06_23\Data\Scripts\Source");
        Add(@"E:\F4SE OG\Tools\Scripting Resources\BaaseGameScripts\Base");
        Add(@"E:\F4SE OG\Tools\PluginEditTool\papyrus\Base");
        return roots;
    }

    public static string? CkWiki() => FirstDir(
        Env("CK_WIKI_PATH"),
        Settings.CkWikiPath,
        App("tools", "ckwiki", "fallout4"),
        @"E:\F4SE OG\docs\Knowledge Materials\Creation Kit Wiki\Fallout 4 Creation Kit Wiki-52183-21-05-20-1621512739\FO4CKWiki_210520\fallout4");

    public static string? Ffmpeg() => FirstFile(
        Env("FFMPEG_PATH"),
        Settings.FfmpegPath,
        App("tools", "audio", "ffmpeg.exe"),
        WhichOnPath("ffmpeg.exe"),
        @"E:\F4SE OG\Tools\Audio Converter\bin\ffmpeg.exe");

    public static string? XwmaEncode() => FirstFile(
        Env("XWMAENCODE_PATH"),
        Settings.XwmaEncodePath,
        App("tools", "audio", "xWMAEncode.exe"),
        WhichOnPath("xWMAEncode.exe"),
        @"E:\F4SE OG\Tools\Audio Converter\bin\xWMAEncode.exe");

    public static string? BmlFuzEncode() => FirstFile(
        Env("BMLFUZENCODE_PATH"),
        App("tools", "audio", "BmlFuzEncode.exe"),
        @"E:\F4SE OG\Tools\Audio Converter\bin\BmlFuzEncode.exe");

    public static string? BmlFuzDecode() => FirstFile(
        Env("BMLFUZDECODE_PATH"),
        App("tools", "audio", "BmlFuzDecode.exe"),
        @"E:\F4SE OG\Tools\Audio Converter\bin\BmlFuzDecode.exe");

    public static string Describe(string tool) => tool switch
    {
        "niftool" => "set NIFTOOL_PATH, or put niftool.exe in tools\\niftool\\ next to the exe",
        "texconv" => "set TEXCONV_PATH, or put Texconvx64.exe in tools\\texconv\\ next to the exe",
        "papyrus" => "install the Creation Kit, or set PAPYRUS_COMPILER_PATH / papyrusCompilerPath in settings.json",
        "archive2" => "install the Creation Kit, or set ARCHIVE2_PATH / archive2Path in settings.json",
        "ckwiki" => "set CK_WIKI_PATH, or put the wiki mirror in tools\\ckwiki\\fallout4\\ next to the exe",
        "ffmpeg" => "set FFMPEG_PATH, or put ffmpeg.exe in tools\\audio\\ next to the exe",
        "xwmaencode" => "set XWMAENCODE_PATH, or put xWMAEncode.exe in tools\\audio\\ next to the exe",
        "bmlfuzencode" => "put BmlFuzEncode.exe in tools\\audio\\ next to the exe",
        "bmlfuzdecode" => "put BmlFuzDecode.exe in tools\\audio\\ next to the exe",
        _ => "",
    };
}
