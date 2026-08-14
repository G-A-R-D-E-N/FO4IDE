namespace FO4RecordEditor.Services;

/// <summary>
/// The one rule the server's catch-all (`MapFallback`) needs: is an unmatched request a browser
/// top-level navigation, or a fetch/asset load?
/// </summary>
/// <remarks>
/// The app is a HashRouter SPA served over a loopback port and driven through `fetch('/rpc')`; it
/// should never perform a top-level navigation. If one happens anyway -- a manual URL, a refresh on a
/// hash-less deep path, or anything that broke the WebView out of the SPA -- serving index.html at a
/// hash-less path boots the SPA onto its default `/` route (the ConflictResolver), which reads as a
/// blank or "secondary" page. Photino/WebKitGTK exposes no cancelable same-window navigation event,
/// so the only place to enforce "the window is only ever on <see cref="AppEntryPath"/>" is here, at
/// the one point every same-origin navigation funnels through -- which also covers WebView2 and
/// WKWebView with no per-host code.
///
/// A navigation is a GET whose Accept advertises HTML. A `fetch` (Accept `*/*`), an EventSource
/// (`text/event-stream`) and an asset load (its own type) are not, and must keep their existing
/// index.html behaviour so a legitimate hash-routed load still works.
/// </remarks>
public static class SpaFallback
{
    /// <summary>The SPA's only real entry, identical to the URL the native window loads with.</summary>
    public const string AppEntryPath = "/#/main";

    /// <summary>True when this unmatched request is a browser top-level navigation.</summary>
    public static bool IsTopLevelNavigation(string? method, string? accept)
    {
        if (!string.Equals(method, "GET", System.StringComparison.OrdinalIgnoreCase)) return false;
        return accept is not null &&
               accept.Contains("text/html", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the catch-all should 302 this request to <see cref="AppEntryPath"/> rather than
    /// serve index.html. That is every top-level navigation EXCEPT the root path.
    /// </summary>
    /// <remarks>
    /// The root exclusion is not cosmetic, it prevents an infinite loop. A browser strips the
    /// fragment before sending, so the SPA entry "/#/main" arrives at the server as GET "/". If "/"
    /// were redirected to "/#/main" the browser would request "/" again, and again. So the root
    /// always falls through to index.html; only genuine sub-paths (a manual deep URL, a WebView that
    /// broke out of the SPA) are redirected back to the entry.
    /// </remarks>
    public static bool ShouldRedirect(string? path, string? method, string? accept)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return false;
        return IsTopLevelNavigation(method, accept);
    }
}
