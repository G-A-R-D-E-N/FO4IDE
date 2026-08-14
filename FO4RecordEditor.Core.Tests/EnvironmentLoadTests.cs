using FluentAssertions;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Core.Tests;

public class EnvironmentLoadTests
{
    [Fact]
    public void TranslateEnvironmentError_MutagenListingsPathFailure_BecomesActionableEnvironmentLoadException()
    {

        var mutagen = new InvalidOperationException(
            "Could not determine plugin listings path for Fallout4. This typically occurs on " +
            "non-Windows platforms where the LocalAppData environment variable is not set.");

        var translated = MutagenLoader.TranslateEnvironmentError(mutagen);

        translated.Should().BeOfType<EnvironmentLoadException>();
        translated.Message.Should().Contain("Open MO2");

        translated.InnerException.Should().BeSameAs(mutagen);
    }

    [Fact]
    public void TranslateEnvironmentError_UnrelatedFailure_IsReturnedUnchanged()
    {

        var unrelated = new System.IO.IOException("a real disk error");

        var translated = MutagenLoader.TranslateEnvironmentError(unrelated);

        translated.Should().BeSameAs(unrelated);
    }
}
