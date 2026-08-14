using System.IO;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

/// <summary>
/// Resolves the external executables and script roots the tool shells out to (niftool, the Creation
/// Kit Papyrus compiler, texconv) on a machine that is not the developer's. Every lookup runs the
/// same chain: environment override, then the user's settings.json, then a copy bundled next to the
/// exe, then the local Fallout 4 / Creation Kit install, and finally the original dev-box paths.
/// The chain is ordered so an explicit choice always beats an auto-detected one.
/// </summary>
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

    /// <summary>Drop the cached settings so a path edited in the GUI takes effect without a restart.</summary>
    public static void Invalidate() => _settings = null;

    /// <summary>
    /// Read every plugin fully into memory rather than memory-mapping the large ones.
    /// The env var FO4RE_FULL_PLUGIN_READS wins over the stored setting, matching the
    /// override-beats-auto-detected ordering the tool paths above use -- and letting a test select
    /// the path without writing to the user's real settings.json.
    /// </summary>
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

    /// <summary>
    /// Auto-detect an executable on the system PATH -- the standard "found it because it's installed"
    /// fallback for tools we don't bundle (ffmpeg is commonly on PATH; a user may keep niftool/texconv/
    /// xWMAEncode there too). Slots into each tool's chain AFTER the bundled copy and BEFORE any
    /// machine-specific install probe, so an explicit setting or a bundled copy still wins.
    /// </summary>
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
                catch { /* a malformed PATH entry -- skip it */ }
            }
        }
        return null;
    }

    /// <summary>
    /// The Fallout 4 install root, from settings, then the registry key the game's installer writes,
    /// then the default Steam library location. Null when Fallout 4 is not installed.
    /// </summary>
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
            catch { /* registry unreadable -- fall through to the disk probe */ }
        }

        // Path segments, not a backslash-joined literal. On Linux a backslash is an ordinary
        // filename character, so the old form probed for a single file literally named
        // "SteamLibrary\steamapps\common\Fallout 4" in each mount root and could never match --
        // which is why auto-detection found nothing at all on the native build.
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
        catch { /* the mount table is not readable; the home probe below still applies */ }

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
                catch { /* drive not ready, or a path this platform rejects */ }
            }
        }
        return null;
    }

    /// <summary>niftool.exe -- our nifly-based CLI. Bundled in the release under tools\niftool\.</summary>
    public static string? Niftool() => FirstFile(
        Env("NIFTOOL_PATH"),
        Settings.NiftoolPath,
        App("tools", "niftool", "niftool.exe"),
        App("niftool", "niftool.exe"),
        WhichOnPath("niftool.exe"),
        @"E:\F4SE OG\Tools\PluginEditTool\tools\niftool\build\windows\x64\release\niftool.exe");

    /// <summary>texconv -- ships inside xEdit's Edit Scripts folder; also bundled in the release.</summary>
    public static string? Texconv() => FirstFile(
        Env("TEXCONV_PATH"),
        Settings.TexconvPath,
        App("tools", "texconv", "Texconvx64.exe"),
        App("tools", "texconv", "Texconv.exe"),
        WhichOnPath("Texconvx64.exe", "Texconv.exe"),
        @"E:\F4SE OG\Tools\PluginEditTool\FO4RecordEditor\TES5Edit-dev-4.1.6\Build\Edit Scripts\Texconvx64.exe",
        @"E:\F4SE OG\Tools\PluginEditTool\FO4RecordEditor\TES5Edit-dev-4.1.6\Build\Edit Scripts\Texconv.exe");

    /// <summary>
    /// The Creation Kit's PapyrusCompiler.exe. It is not redistributable, so it is never bundled --
    /// it is found in the user's own Fallout 4 install.
    /// </summary>
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

    /// <summary>
    /// The Creation Kit's Archive2.exe (BA2 packer/extractor). Same reasoning as PapyrusCompiler: it
    /// is not redistributable, so it is never bundled -- found in the user's own Fallout 4/CK install.
    ///
    /// Nothing REQUIRES this any more. Reading never did (Mutagen reads BA2/BSA natively), and
    /// packing is now done in process by Ba2Packer; Archive2 is only reached for a DDS (texture)
    /// archive, or when a caller explicitly asks for it with use_archive2.
    ///
    /// The auto-detect (Fallout4Root-derived) paths are checked LAST, not first, on purpose: a
    /// Next-Gen Archive2.exe (reports "Version 1.1.0.5", writes BA2 header version 8) can be
    /// unreadable here, because BA2FileEntry's version>=7 branch (Ba2Reader.cs) computed
    /// Compressed=false for data still zlib-compressed on disk and GetBytes() returned raw
    /// compressed bytes. A known-good BA2Packer copy (reports "Version 1.1.0.4", writes header
    /// version 1) is checked first for that reason. ArchiveService.Pack also verifies the header
    /// version of whatever it produced and fails loudly rather than shipping a silently-corrupt
    /// archive if this ever drifts again.
    /// </summary>
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

    /// <summary>
    /// Every base-script source root that exists, highest priority first. F4SE must precede the
    /// vanilla base: its Game.psc/Actor.psc declare extra natives that the vanilla ones lack.
    /// </summary>
    /// <remarks>
    /// Never throws. Every caller uses this to build an import path, and a probe failing on one
    /// unreadable mount must degrade to a shorter list rather than take a compile down -- the
    /// compiler already has an honest answer for "no base scripts found".
    /// </remarks>
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
            catch { /* a path this platform rejects outright is simply not a root */ }
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

    /// <summary>
    /// The offline Creation Kit Wiki HTML mirror root that papyrus_function_lookup / papyrus_script_info
    /// read from. Ships bundled with the release package (package.ps1) under tools\ckwiki\fallout4\, so
    /// this resolves out of the box with no launch flag or Settings edit needed; CkWikiPath/CK_WIKI_PATH
    /// only matter if you want to point at a different or newer mirror.
    /// </summary>
    public static string? CkWiki() => FirstDir(
        Env("CK_WIKI_PATH"),
        Settings.CkWikiPath,
        App("tools", "ckwiki", "fallout4"),
        @"E:\F4SE OG\docs\Knowledge Materials\Creation Kit Wiki\Fallout 4 Creation Kit Wiki-52183-21-05-20-1621512739\FO4CKWiki_210520\fallout4");

    /// <summary>
    /// ffmpeg -- decodes any input audio (mp3/flac/ogg/m4a/wma/...) to PCM WAV for xWMAEncode, and the
    /// reverse (xwm-derived WAV to whatever output format is asked for). Bundled under tools\audio\.
    /// </summary>
    public static string? Ffmpeg() => FirstFile(
        Env("FFMPEG_PATH"),
        Settings.FfmpegPath,
        App("tools", "audio", "ffmpeg.exe"),
        WhichOnPath("ffmpeg.exe"),
        @"E:\F4SE OG\Tools\Audio Converter\bin\ffmpeg.exe");

    /// <summary>Microsoft's xWMAEncode.exe -- the only thing that actually reads/writes xWMA. Encodes
    /// PCM WAV to xWMA and decodes xWMA back to PCM WAV depending on which direction the input is.
    /// Bundled under tools\audio\.</summary>
    public static string? XwmaEncode() => FirstFile(
        Env("XWMAENCODE_PATH"),
        Settings.XwmaEncodePath,
        App("tools", "audio", "xWMAEncode.exe"),
        WhichOnPath("xWMAEncode.exe"),
        @"E:\F4SE OG\Tools\Audio Converter\bin\xWMAEncode.exe");

    /// <summary>BmlFuzEncode.exe (BowmoreLover) -- packs an xwm + a lip file into a .fuz voice
    /// container. Bundled-only: single-purpose, nobody keeps their own copy to point at.</summary>
    public static string? BmlFuzEncode() => FirstFile(
        Env("BMLFUZENCODE_PATH"),
        App("tools", "audio", "BmlFuzEncode.exe"),
        @"E:\F4SE OG\Tools\Audio Converter\bin\BmlFuzEncode.exe");

    /// <summary>BmlFuzDecode.exe (BowmoreLover) -- splits a .fuz voice container back into its xwm and
    /// lip file. Bundled-only, same reasoning as BmlFuzEncode.</summary>
    public static string? BmlFuzDecode() => FirstFile(
        Env("BMLFUZDECODE_PATH"),
        App("tools", "audio", "BmlFuzDecode.exe"),
        @"E:\F4SE OG\Tools\Audio Converter\bin\BmlFuzDecode.exe");

    /// <summary>A one-line "here is where I looked" for the not-found errors the AI reads back.</summary>
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
