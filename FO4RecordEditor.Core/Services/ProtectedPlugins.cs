using System.IO;

namespace FO4RecordEditor.Services;

/// <summary>
/// The vanilla game masters. Single source of truth -- read-only scanners treat these as
/// always-resolvable, and the write layer refuses to open or overwrite them.
///
/// These files ship with the game and are not regenerable by the user: overwriting one means a
/// Steam re-validate or a reinstall, and any mod built against the damaged copy is silently wrong
/// until then. A typo in a plugin name was previously enough to do it.
/// </summary>
public static class ProtectedPlugins
{
    public static readonly IReadOnlySet<string> VanillaMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm",
        "DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm",
        "DLCUltraHighResolution.esm",
    };

    /// <summary>True if <paramref name="pluginFileName"/> is a vanilla master (name only, no path).</summary>
    public static bool IsProtected(string? pluginFileName) =>
        !string.IsNullOrWhiteSpace(pluginFileName) && VanillaMasters.Contains(pluginFileName);

    /// <summary>True if <paramref name="path"/> points at a file named like a vanilla master.</summary>
    public static bool PathIsProtected(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IsProtected(Path.GetFileName(path));

    public static string RefusalMessage(string pluginFileName) =>
        $"'{pluginFileName}' is a vanilla game master and is write-protected. " +
        "Overwriting it would require a game re-validate or reinstall to undo. " +
        "Create or open a patch plugin instead (create_plugin 'MyPatch.esp'), then use " +
        "copy_as_override to bring the vanilla records you want to change into it.";

    private static readonly string[] AllowedExtensions = { ".esp", ".esm", ".esl" };

    /// <summary>
    /// Validates an explicit save destination. Returns null when the path is acceptable, or an
    /// explanation when it is not. Guards the extension, path traversal, and vanilla-master names,
    /// because SavePlugin creates missing parent directories and then File.Replace/File.Move over
    /// whatever is already at the destination.
    /// </summary>
    public static string? ValidateSavePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Save path is empty.";

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) { return $"Invalid save path '{path}': {ex.Message}"; }

        // Checks below run on the NORMALIZED path, so '...\mods\..\Data\Fallout4.esm' is caught by
        // the protected-name check rather than sneaking through as a non-matching literal.
        var fileName = Path.GetFileName(full);
        if (string.IsNullOrWhiteSpace(fileName))
            return $"Save path '{path}' does not name a file.";

        var ext = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            return $"Refusing to save to '{fileName}': a plugin must be written as .esp, .esm or .esl (got '{ext}').";

        if (IsProtected(fileName)) return RefusalMessage(fileName);

        return null;
    }
}
