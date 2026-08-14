using System.IO;

namespace FO4RecordEditor.Services;









public static class DebugLog
{
    private static readonly object _lock = new();
    private static string? _path;
    private static bool _tried;
    private const long MaxBytes = 8 * 1024 * 1024;

    public static string? Path => _path;

    public static void Init()
    {
        lock (_lock)
        {
            if (_tried) return;
            _tried = true;

            var candidates = new[]
            {
                System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "FO4RecordEditor", "debug.log"),
                System.IO.Path.Combine(AppContext.BaseDirectory, "FO4RecordEditor.debug.log"),
            };

            foreach (var p in candidates)
            {
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);

                    if (File.Exists(p) && new FileInfo(p).Length > MaxBytes)
                    {
                        var bak = p + ".old";
                        File.Delete(bak);
                        File.Move(p, bak);
                    }
                    File.AppendAllText(p,
                        $"{Environment.NewLine}=== session {DateTime.Now:yyyy-MM-dd HH:mm:ss} " +
                        $"pid {Environment.ProcessId} ==={Environment.NewLine}");
                    _path = p;
                    return;
                }
                catch {  }
            }
        }
    }

    public static void Write(string level, string category, string message, string? detail = null)
    {
        if (!_tried) Init();
        if (_path == null) return;

        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [t{Environment.CurrentManagedThreadId,-3}] " +
                   $"[{level,-7}] [{category,-8}] {message}" +
                   (string.IsNullOrEmpty(detail) ? "" : $"  | {detail}");

        lock (_lock)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); } catch {  }
        }
    }

    public static void Info(string category, string message, string? detail = null) => Write("INFO", category, message, detail);
    public static void Debug(string category, string message, string? detail = null) => Write("DEBUG", category, message, detail);


    public static void Interop(string method, string? args = null) =>
        Write("DEBUG", "Interop", args == null ? method + "()" : $"{method}({Trunc(args, 400)})");


    public static void Exception(string context, Exception ex) =>
        Write("ERROR", "Error", $"{context} threw {ex.GetType().Name}: {ex.Message}", ex.ToString());

    private static string Trunc(string s, int max) => s.Length <= max ? s : s[..max] + "…";



    public static string Guard(string method, Func<string> body, string? args = null)
    {
        Interop(method, args);
        try { return body(); }
        catch (Exception ex) { Exception(method, ex); throw; }
    }

    public static void Guard(string method, Action body, string? args = null)
    {
        Interop(method, args);
        try { body(); }
        catch (Exception ex) { Exception(method, ex); throw; }
    }

    public static async System.Threading.Tasks.Task<string> GuardAsync(
        string method, Func<System.Threading.Tasks.Task<string>> body, string? args = null)
    {
        Interop(method, args);
        try { return await body(); }
        catch (Exception ex) { Exception(method, ex); throw; }
    }

    public static async System.Threading.Tasks.Task GuardAsync(
        string method, Func<System.Threading.Tasks.Task> body, string? args = null)
    {
        Interop(method, args);
        try { await body(); }
        catch (Exception ex) { Exception(method, ex); throw; }
    }
}
