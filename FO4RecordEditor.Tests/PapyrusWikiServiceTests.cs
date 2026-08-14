using System.IO;
using FluentAssertions;
using FO4RecordEditor.Services;
using Xunit;

namespace FO4RecordEditor.Tests;

// Fixture HTML mirrors the CK wiki's real exported MediaWiki markup verbatim (verified against
// GetBaseObject_-_ActiveMagicEffect.html and ActiveMagicEffect_Script.html in the actual mirror
// before writing PapyrusWikiService's parser), so these tests exercise the real parsing logic
// against realistic input rather than a simplified stand-in.
public class PapyrusWikiServiceTests
{
    private static string FunctionPageHtml() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <title>GetBaseObject - ActiveMagicEffect</title>
        <body>
        <h1 id="firstHeading" class="firstHeading" lang="en">GetBaseObject - ActiveMagicEffect</h1>
        <div id="mw-content-text" lang="en" dir="ltr" class="mw-content-ltr"><p><b>Member of:</b> <a href="ActiveMagicEffect_Script.html" title="ActiveMagicEffect Script">ActiveMagicEffect Script</a>
        </p><p>Obtains the <a href="MagicEffect_Script.html" title="MagicEffect Script">MagicEffect</a> this active magic effect is based on.
        </p>
        <h2><span class="mw-headline" id="Syntax">Syntax</span></h2>
        <div class="mw-geshi"><pre class="de1"><span class="kw3">MagicEffect</span> <span class="kw1">Function</span> <span class="kw5">GetBaseObject</span><span class="br0">(</span><span class="br0">)</span></pre></div>
        <h2><span class="mw-headline" id="Parameters">Parameters</span></h2>
        <p>None
        </p>
        <h2><span class="mw-headline" id="Return_Value">Return Value</span></h2>
        <p>The <a href="MagicEffect_Script.html" title="MagicEffect Script">MagicEffect</a> this active effect is based on.
        </p>
        <h2><span class="mw-headline" id="Examples">Examples</span></h2>
        <div class="mw-geshi"><pre class="de1">example code here</pre></div>
        <h2><span class="mw-headline" id="Caveat">Caveat</span></h2>
        <p>It is possible the effect ceases to exist before this call returns.
        </p>
        <h2><span class="mw-headline" id="See_Also">See Also</span></h2>
        <div id="catlinks" class="catlinks">cats</div>
        </div>
        </body>
        """;

    private static string ScriptPageHtml() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
        <title>ActiveMagicEffect Script</title>
        <body>
        <h1 id="firstHeading" class="firstHeading" lang="en">ActiveMagicEffect Script</h1>
        <div id="mw-content-text" lang="en" dir="ltr" class="mw-content-ltr"><p><b>Extends:</b> <a href="ScriptObject_Script.html" title="ScriptObject Script">ScriptObject</a>
        <br>
        <b>Editor:</b> <a href="Magic_Effect.html" title="Magic Effect">Magic Effect</a>
        </p><p>Script for the manipulation of active magic effects.
        </p>
        <h2><span class="mw-headline" id="Definition">Definition</span></h2>
        <div class="mw-geshi"><pre class="de1"><span class="kw1">ScriptName</span> ActiveMagicEffect extends ScriptObject Native Hidden</pre></div>
        <h2><span class="mw-headline" id="Properties">Properties</span></h2>
        <p>None
        </p>
        <h2><span class="mw-headline" id="Global_Functions">Global_Functions</span></h2>
        <p>None
        </p>
        <h2><span class="mw-headline" id="Member_Functions">Member Functions</span></h2>
        <ul><li>Function <a href="Dispel_-_ActiveMagicEffect.html" title="Dispel - ActiveMagicEffect">Dispel</a>()
        <ul><li>Dispels this active magic effect.</li></ul></li>
        <li>MagicEffect Function <a href="GetBaseObject_-_ActiveMagicEffect.html" title="GetBaseObject - ActiveMagicEffect">GetBaseObject</a>()
        <ul><li>Obtains the MagicEffect this active magic effect is based on.</li></ul></li></ul>
        <h2><span class="mw-headline" id="Events">Events</span></h2>
        <ul><li>Event <a href="OnEffectStart_-_ActiveMagicEffect.html" title="OnEffectStart - ActiveMagicEffect">OnEffectStart</a>(Actor akTarget, Actor akCaster)
        <ul><li>Event received when this effect starts.</li></ul></li></ul>
        <h2><span class="mw-headline" id="Notes">Notes</span></h2>
        <ul><li>Notes here.</li></ul>
        <h2><span class="mw-headline" id="See_Also">See Also</span></h2>
        <div id="catlinks" class="catlinks">cats</div>
        </div>
        </body>
        """;

    private static string MakeWikiRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"CkWikiTest_{Guid.NewGuid():N}", "fallout4");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "GetBaseObject_-_ActiveMagicEffect.html"), FunctionPageHtml());
        File.WriteAllText(Path.Combine(root, "ActiveMagicEffect_Script.html"), ScriptPageHtml());
        return root;
    }

    [Fact]
    public void LookupFunction_ExtractsSyntaxAndReturnValue()
    {
        var root = MakeWikiRoot();
        try
        {
            var result = PapyrusWikiService.LookupFunction(root, "ActiveMagicEffect", "GetBaseObject");
            result.Should().Contain("Member of: ActiveMagicEffect Script");
            result.Should().Contain("Syntax:").And.Contain("MagicEffect Function GetBaseObject ( )");
            result.Should().Contain("Parameters: None");
            result.Should().Contain("Return Value:").And.Contain("this active effect is based on");
            result.Should().Contain("Caveat:").And.Contain("ceases to exist");
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(root)!, true); } catch { } }
    }

    [Fact]
    public void LookupFunction_WithoutScript_FindsUniqueMatch()
    {
        var root = MakeWikiRoot();
        try
        {
            PapyrusWikiService.LookupFunction(root, "", "GetBaseObject")
                .Should().Contain("Syntax:");
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(root)!, true); } catch { } }
    }

    [Fact]
    public void LookupFunction_UnknownFunction_ReturnsHelpfulMessage()
    {
        var root = MakeWikiRoot();
        try
        {
            PapyrusWikiService.LookupFunction(root, "", "TotallyMadeUpFunction")
                .Should().Contain("No CK wiki page found");
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(root)!, true); } catch { } }
    }

    [Fact]
    public void LookupFunction_NoWikiConfigured_ReturnsConfigError()
    {
        PapyrusWikiService.LookupFunction("", "", "GetBaseObject")
            .Should().Contain("No CK wiki mirror configured").And.Contain("--ck-wiki");
    }

    [Fact]
    public void LookupScriptInfo_ExtractsExtendsAndMemberFunctions()
    {
        var root = MakeWikiRoot();
        try
        {
            var result = PapyrusWikiService.LookupScriptInfo(root, "ActiveMagicEffect");
            result.Should().Contain("Extends: ScriptObject");
            result.Should().Contain("Member Functions:").And.Contain("GetBaseObject").And.Contain("Dispel");
            result.Should().Contain("Events:").And.Contain("OnEffectStart");
            result.Should().NotContain("Global Functions:", "the page's Global Functions section is 'None' and should be omitted");
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(root)!, true); } catch { } }
    }

    [Fact]
    public void LookupScriptInfo_AcceptsTrailingScriptSuffix()
    {
        var root = MakeWikiRoot();
        try
        {
            PapyrusWikiService.LookupScriptInfo(root, "ActiveMagicEffect_Script")
                .Should().Contain("Extends: ScriptObject");
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(root)!, true); } catch { } }
    }
}
