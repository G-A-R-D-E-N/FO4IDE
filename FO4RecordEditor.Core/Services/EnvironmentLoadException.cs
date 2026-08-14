namespace FO4RecordEditor.Services;

public sealed class EnvironmentLoadException : System.Exception
{
    public EnvironmentLoadException(string message, System.Exception? inner = null) : base(message, inner) { }
}

public static partial class MutagenLoader
{

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
