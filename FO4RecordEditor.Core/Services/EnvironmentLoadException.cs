namespace FO4RecordEditor.Services;

/// <summary>
/// A clear, user-facing failure from building the game environment. Its <see cref="System.Exception.Message"/>
/// is written to be shown verbatim in the UI, so it must stay actionable rather than echoing Mutagen's
/// internal wording. The original cause is always kept as the inner exception for the debug log.
/// </summary>
public sealed class EnvironmentLoadException : System.Exception
{
    public EnvironmentLoadException(string message, System.Exception? inner = null) : base(message, inner) { }
}

public static partial class MutagenLoader
{
    /// <summary>
    /// Translate a raw environment-build failure into an actionable message where we recognise it,
    /// and leave everything else exactly as-is.
    /// </summary>
    /// <remarks>
    /// The one case we recognise is Mutagen failing to locate the game's load order file. On Linux
    /// there is no %LocalAppData%\Fallout4\Plugins.txt and often no discoverable Proton prefix, so
    /// <c>GameEnvironment.Typical...Build()</c> throws an <see cref="System.InvalidOperationException"/>
    /// from PluginListingsPathContext. That is not a broken install -- it is auto-detect asking for a
    /// file the platform does not have -- so the fix is to tell the user to load their modlist with
    /// "Open MO2", which reads its own plugin list and never touches that path.
    ///
    /// Any other failure (a corrupt plugin, a disk error, an out-of-memory) is returned untouched:
    /// rewriting a genuine error into "use Open MO2" would hide the real cause.
    /// </remarks>
    public static System.Exception TranslateEnvironmentError(System.Exception ex)
    {
        if (ex is System.InvalidOperationException &&
            (ex.Message.Contains("plugin listings path", System.StringComparison.OrdinalIgnoreCase) ||
             ex.Message.Contains("LocalAppData", System.StringComparison.OrdinalIgnoreCase)))
        {
            return new EnvironmentLoadException(
                "Load Env could not auto-detect a Fallout 4 load order. On Linux there is no " +
                "game-managed load order file (Plugins.txt) to read outside a Proton prefix, so " +
                "auto-detect cannot build an environment here. Use \"Open MO2\" to load your modlist " +
                "instead -- it reads the profile's own plugin list directly.",
                ex);
        }

        return ex;
    }
}
