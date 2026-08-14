using System.Reflection;
using FO4RecordEditor.Services;
using Newtonsoft.Json.Linq;

namespace FO4RecordEditor.Server;

/// <summary>
/// Stands in for WebView2's AddHostObjectToScript.
///
/// In the WPF shell the React code calls window.chrome.webview.hostObjects.backend.GetConflicts(),
/// and WebView2 marshals that across as a COM call returning a Promise. Here the same call arrives
/// as POST /rpc {"target":"backend","method":"GetConflicts","args":[]} and is dispatched by
/// reflection onto the same interop instance. The registered names must match MainWindow's
/// AddHostObjectToScript names exactly, because the frontend addresses them by name.
/// </summary>
public sealed class RpcDispatcher
{
    private readonly Dictionary<string, object> _targets = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, object target) => _targets[name] = target;

    public IReadOnlyCollection<string> TargetNames => _targets.Keys;

    public sealed record Request(string Target, string Method, JArray? Args);

    public async Task<object?> InvokeAsync(Request req)
    {
        if (!_targets.TryGetValue(req.Target ?? "", out var target))
            throw new MissingMethodException($"No such host object: '{req.Target}'. Known: {string.Join(", ", _targets.Keys)}");

        var args = req.Args ?? new JArray();
        var method = Resolve(target.GetType(), req.Method, args.Count)
            ?? throw new MissingMethodException(
                $"{req.Target}.{req.Method} takes no {args.Count}-argument overload.");

        var ps = method.GetParameters();
        var call = new object?[ps.Length];
        for (int i = 0; i < ps.Length; i++)
        {
            if (i < args.Count && args[i].Type != JTokenType.Null)
                call[i] = args[i].ToObject(ps[i].ParameterType);
            else if (ps[i].HasDefaultValue) call[i] = ps[i].DefaultValue;
            else call[i] = ps[i].ParameterType.IsValueType
                ? Activator.CreateInstance(ps[i].ParameterType) : null;
        }

        var result = method.Invoke(target, call);

        // Interop methods return string, bool, void, Task or Task<T>. Unwrap so the JS side sees
        // the same value the WebView2 proxy would have resolved its Promise with.
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            var t = task.GetType();
            if (!t.IsGenericType) return null;
            return t.GetProperty("Result")?.GetValue(task);
        }
        return result;
    }

    /// <summary>Prefer an exact arity match; fall back to one reachable with defaults, which is how
    /// the JS side gets away with omitting trailing optional parameters.</summary>
    private static MethodInfo? Resolve(Type type, string name, int argc)
    {
        MethodInfo? loose = null;
        foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!string.Equals(m.Name, name, StringComparison.Ordinal)) continue;
            var ps = m.GetParameters();
            if (ps.Length == argc) return m;
            if (argc < ps.Length && ps.Skip(argc).All(p => p.HasDefaultValue)) loose ??= m;
        }
        return loose;
    }
}
