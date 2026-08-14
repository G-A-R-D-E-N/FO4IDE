using FluentAssertions;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Core.Tests;

/// <summary>
/// The server's catch-all has one decision to make: is an unmatched request a browser top-level
/// navigation (which must be sent back to the SPA entry so the WebView can never land on a blank or
/// secondary page), or a fetch/asset load (which keeps serving index.html as before)? Photino and
/// WebKitGTK expose no cancelable same-window navigation event, so this same-origin rule is what
/// enforces "the window is only ever on /#/main", and it does so for WebView2 and WKWebView too.
/// </summary>
public class SpaFallbackTests
{
    [Fact]
    public void BrowserNavigation_GetWithHtmlAccept_IsTopLevelNavigation()
    {
        // What a browser sends for a real page navigation / address-bar load.
        const string accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,*/*;q=0.8";

        SpaFallback.IsTopLevelNavigation("GET", accept).Should().BeTrue();
    }

    [Fact]
    public void Fetch_GetWithNonHtmlAccept_IsNotNavigation()
    {
        // fetch('/rpc') defaults to Accept: */*, an EventSource to text/event-stream, an asset to its
        // own type -- none of these are page navigations and must not be redirected.
        SpaFallback.IsTopLevelNavigation("GET", "*/*").Should().BeFalse();
        SpaFallback.IsTopLevelNavigation("GET", "text/event-stream").Should().BeFalse();
        SpaFallback.IsTopLevelNavigation("GET", "application/json").Should().BeFalse();
    }

    [Fact]
    public void NonGet_IsNeverANavigation()
    {
        // /rpc is POST with no Accept constraint; a POST is never a top-level navigation.
        SpaFallback.IsTopLevelNavigation("POST", "text/html").Should().BeFalse();
    }

    [Fact]
    public void MissingAccept_IsTreatedConservativelyAsNotNavigation()
    {
        // No Accept header -> do not redirect; fall through to the existing index.html behaviour.
        SpaFallback.IsTopLevelNavigation("GET", null).Should().BeFalse();
    }

    [Fact]
    public void Decision_IsCaseInsensitive_OnBothMethodAndAccept()
    {
        SpaFallback.IsTopLevelNavigation("get", "TEXT/HTML").Should().BeTrue();
    }

    [Fact]
    public void AppEntryPath_MatchesTheUrlTheNativeWindowLoads()
    {
        // The redirect target must equal the hash route the Photino window opens with, or a redirect
        // would bounce the user onto the wrong view.
        SpaFallback.AppEntryPath.Should().Be("/#/main");
    }

    [Fact]
    public void ShouldRedirect_StrayDeepNavigation_IsTrue()
    {
        SpaFallback.ShouldRedirect("/main", "GET", "text/html").Should().BeTrue();
        SpaFallback.ShouldRedirect("/record/000800", "GET", "text/html").Should().BeTrue();
    }

    [Fact]
    public void ShouldRedirect_RootPath_IsFalse_ToAvoidARedirectLoop()
    {
        // The browser strips the hash, so the SPA entry "/#/main" reaches the server as GET "/".
        // Redirecting "/" to "/#/main" would just request "/" again -- an infinite loop. The root
        // must always fall through to index.html.
        SpaFallback.ShouldRedirect("/", "GET", "text/html").Should().BeFalse();
        SpaFallback.ShouldRedirect("", "GET", "text/html").Should().BeFalse();
        SpaFallback.ShouldRedirect(null, "GET", "text/html").Should().BeFalse();
    }

    [Fact]
    public void ShouldRedirect_NonNavigationRequests_AreFalse()
    {
        SpaFallback.ShouldRedirect("/main", "GET", "*/*").Should().BeFalse();        // a fetch
        SpaFallback.ShouldRedirect("/main", "POST", "text/html").Should().BeFalse(); // not a navigation
    }
}
