namespace FO4RecordEditor.Services;



















public static class SpaFallback
{

    public const string AppEntryPath = "/#/main";


    public static bool IsTopLevelNavigation(string? method, string? accept)
    {
        if (!string.Equals(method, "GET", System.StringComparison.OrdinalIgnoreCase)) return false;
        return accept is not null &&
               accept.Contains("text/html", System.StringComparison.OrdinalIgnoreCase);
    }












    public static bool ShouldRedirect(string? path, string? method, string? accept)
    {
        if (string.IsNullOrEmpty(path) || path == "/") return false;
        return IsTopLevelNavigation(method, accept);
    }
}
