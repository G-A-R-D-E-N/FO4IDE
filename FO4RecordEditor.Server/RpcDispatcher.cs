using System.Reflection;
using FO4RecordEditor.Services;
using Newtonsoft.Json.Linq;

namespace FO4RecordEditor.Server;










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



        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            var t = task.GetType();
            if (!t.IsGenericType) return null;
            return t.GetProperty("Result")?.GetValue(task);
        }
        return result;
    }



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
