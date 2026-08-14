using System.IO;

namespace FO4RecordEditor.Services;









public static class ProtectedPlugins
{
    public static readonly IReadOnlySet<string> VanillaMasters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Fallout4.esm", "DLCRobot.esm", "DLCworkshop01.esm", "DLCCoast.esm",
        "DLCworkshop02.esm", "DLCworkshop03.esm", "DLCNukaWorld.esm",
        "DLCUltraHighResolution.esm",
    };


    public static bool IsProtected(string? pluginFileName) =>
        !string.IsNullOrWhiteSpace(pluginFileName) && VanillaMasters.Contains(pluginFileName);


    public static bool PathIsProtected(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IsProtected(Path.GetFileName(path));

    public static string RefusalMessage(string pluginFileName) =>
        $"'{pluginFileName}' is a vanilla game master and is write-protected. " +
        "Overwriting it would require a game re-validate or reinstall to undo. " +
        "Create or open a patch plugin instead (create_plugin 'MyPatch.esp'), then use " +
        "copy_as_override to bring the vanilla records you want to change into it.";

    private static readonly string[] AllowedExtensions = { ".esp", ".esm", ".esl" };







    public static string? ValidateSavePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "Save path is empty.";

        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) { return $"Invalid save path '{path}': {ex.Message}"; }



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
