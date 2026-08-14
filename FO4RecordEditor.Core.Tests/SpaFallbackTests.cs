using FluentAssertions;
using FO4RecordEditor.Services;

namespace FO4RecordEditor.Core.Tests;








public class SpaFallbackTests
{
    [Fact]
    public void BrowserNavigation_GetWithHtmlAccept_IsTopLevelNavigation()
    {

        const string accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,*/*;q=0.8";

        SpaFallback.IsTopLevelNavigation("GET", accept).Should().BeTrue();
    }

    [Fact]
    public void Fetch_GetWithNonHtmlAccept_IsNotNavigation()
    {


        SpaFallback.IsTopLevelNavigation("GET", "*/*").Should().BeFalse();
        SpaFallback.IsTopLevelNavigation("GET", "text/event-stream").Should().BeFalse();
        SpaFallback.IsTopLevelNavigation("GET", "application/json").Should().BeFalse();
    }

    [Fact]
    public void NonGet_IsNeverANavigation()
    {

        SpaFallback.IsTopLevelNavigation("POST", "text/html").Should().BeFalse();
    }

    [Fact]
    public void MissingAccept_IsTreatedConservativelyAsNotNavigation()
    {

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



        SpaFallback.ShouldRedirect("/", "GET", "text/html").Should().BeFalse();
        SpaFallback.ShouldRedirect("", "GET", "text/html").Should().BeFalse();
        SpaFallback.ShouldRedirect(null, "GET", "text/html").Should().BeFalse();
    }

    [Fact]
    public void ShouldRedirect_NonNavigationRequests_AreFalse()
    {
        SpaFallback.ShouldRedirect("/main", "GET", "*/*").Should().BeFalse();
        SpaFallback.ShouldRedirect("/main", "POST", "text/html").Should().BeFalse();
    }
}
