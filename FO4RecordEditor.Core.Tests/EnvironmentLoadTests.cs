using FluentAssertions;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// The "Load Env" (auto-detect) path builds a Mutagen GameEnvironment, which on Linux throws because
/// there is no game-managed load order file to read: no %LocalAppData%\Fallout4\Plugins.txt and no
/// discoverable Proton prefix. Before this fix the raw Mutagen exception was swallowed by
/// ShellViewModel.LoadEnvironmentAsync and the UI simply hung at "Initializing Game Environment...".
///
/// MutagenLoader.TranslateEnvironmentError turns that one specific failure into an actionable message
/// that steers the user to "Open MO2" (the supported Linux path), while leaving every unrelated
/// failure untouched so a genuine error is never rewritten into misleading advice.
/// </summary>
public class EnvironmentLoadTests
{
    [Fact]
    public void TranslateEnvironmentError_MutagenListingsPathFailure_BecomesActionableEnvironmentLoadException()
    {
        // The exact message Mutagen's PluginListingsPathContext throws on a non-Windows box.
        var mutagen = new InvalidOperationException(
            "Could not determine plugin listings path for Fallout4. This typically occurs on " +
            "non-Windows platforms where the LocalAppData environment variable is not set.");

        var translated = MutagenLoader.TranslateEnvironmentError(mutagen);

        translated.Should().BeOfType<EnvironmentLoadException>();
        translated.Message.Should().Contain("Open MO2");
        // The original cause is preserved for the debug log, not thrown away.
        translated.InnerException.Should().BeSameAs(mutagen);
    }

    [Fact]
    public void TranslateEnvironmentError_UnrelatedFailure_IsReturnedUnchanged()
    {
        // A genuine problem (a corrupt plugin, a disk error) must NOT be rewritten into "use Open MO2":
        // that would hide the real cause. Only the known listings-path failure is translated.
        var unrelated = new System.IO.IOException("a real disk error");

        var translated = MutagenLoader.TranslateEnvironmentError(unrelated);

        translated.Should().BeSameAs(unrelated);
    }
}
