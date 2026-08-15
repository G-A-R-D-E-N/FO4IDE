using System.Collections;
using System.Reflection;
using System.Text.Json;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Services;

public static class ElementService
{
    public static string DescribeElement(object? env, string plugin, string recordId, string path)
    {
        var rec = FindRecordForRead(env, plugin, recordId);
        if (rec == null) return JsonSerializer.Serialize(new { error = $"Record '{recordId}' not found." });

        if (!TryDescribe(rec, path, out var d, out var err))
            return JsonSerializer.Serialize(new { error = err });

        var templates = d.ElementType == null ? Array.Empty<string>() : TemplatesFor(d.ElementType);

        return JsonSerializer.Serialize(new
        {
            canAdd = d.ElementType != null && templates.Length > 0,
            templates,
            elementType = d.ElementType == null ? "" : FriendlyTypeName(d.ElementType),
            canRemove = d.Index >= 0,
            canMoveUp = d.Index > 0,
            canMoveDown = d.Index >= 0 && d.Index < d.Count - 1,
            canClear = d.IsList && d.Count > 0,
            count = d.Count,
        });
    }

    private struct Described
    {
        public Type? ElementType;
        public int Index;
        public int Count;
        public bool IsList;
    }

    private static bool TryDescribe(IMajorRecordGetter rec, string path, out Described d, out string error)
    {
        d = new Described { Index = -1 }; error = "";
        Type? curType = rec.Registration.ClassType;
        object? curVal = rec;

        foreach (var (name, index) in SplitPath(path))
        {
            if (curType == null) { error = $"'{path}' does not resolve on {rec.Registration.Name}."; return false; }

            var prop = curType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) { error = $"No field '{name}' on {curType.Name} (path '{path}')."; return false; }
            curType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            var valProp = curVal?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            curVal = valProp?.GetValue(curVal);

            var elem = SequenceElementType(curType);
            if (elem != null)
            {
                d.ElementType = elem;
                d.Index = -1;
                d.Count = CountOf(curVal);
                d.IsList = true;
            }
            else d.IsList = false;

            if (index is { } i)
            {
                if (elem == null) { error = $"'{name}' is not a list, so '{name}[{i}]' is meaningless."; return false; }
                if (i < 0 || i >= d.Count) { error = $"'{name}[{i}]' is out of range ({d.Count} item(s))."; return false; }
                d.Index = i;
                d.IsList = false;
                curType = elem;
                curVal = ItemAt(curVal, i);
                if (curVal != null) curType = curVal.GetType();
            }
        }
        return true;
    }

    private static Type? SequenceElementType(Type t)
    {
        if (t == typeof(string)) return null;
        foreach (var iface in new[] { t }.Concat(t.GetInterfaces()))
        {
            if (!iface.IsGenericType) continue;
            var g = iface.GetGenericTypeDefinition();
            if (g == typeof(IList<>) || g == typeof(IReadOnlyList<>) || g == typeof(ICollection<>))
                return iface.GetGenericArguments()[0];
        }
        return null;
    }

    private static int CountOf(object? v) => v switch
    {
        null => 0,
        IList l => l.Count,
        IEnumerable e => e.Cast<object?>().Count(),
        _ => 0,
    };

    private static object? ItemAt(object? v, int i)
    {
        if (v is IList l) return i < l.Count ? l[i] : null;
        if (v is IEnumerable e) return e.Cast<object?>().Skip(i).FirstOrDefault();
        return null;
    }

    public static string AddElement(string plugin, string recordId, string path, string? template, object? env)
    {
        var mod = WriteService.GetMutableFor(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var rec = WriteService.FindRecordIn(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!TryResolve(rec, path, initializeNullLists: true, out var r, out var err)) return ToolError.Fail(err);
        if (r.TargetList is not { } list) return ToolError.Fail($"'{path}' is not inside a list, so there is nothing to add to.");

        var elemType = ElementTypeOf(list);
        var chosen = PickTemplate(elemType, template, out var pickErr);
        if (chosen == null) return ToolError.Fail(pickErr);

        object? instance;
        try { instance = CreateInstance(chosen); }
        catch (Exception ex) { return ToolError.Fail($"Could not create a {FriendlyTypeName(chosen)}: {ex.Message}"); }
        if (instance == null) return ToolError.Fail($"Could not create a {FriendlyTypeName(chosen)}.");

        int at = r.Index >= 0 ? Math.Min(r.Index, list.Count) : list.Count;
        try { list.Insert(at, instance); }
        catch (Exception ex) { return ToolError.Fail($"Could not insert into '{path}': {ex.Message}"); }

        var (name, _) = WriteService.SplitPlugin(plugin);
        MutagenLoader.InvalidateModIndex(name); WriteService.RaiseChanged(name);
        return $"Added a {FriendlyTypeName(chosen)} at {ContainerPath(path)}[{at}] on {recordId} in {name} " +
               $"(now {list.Count}). Edit its fields in the grid, then save_plugin.";
    }

    public static string RemoveElement(string plugin, string recordId, string path, object? env)
    {
        var mod = WriteService.GetMutableFor(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var rec = WriteService.FindRecordIn(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!TryResolve(rec, path, initializeNullLists: false, out var r, out var err)) return ToolError.Fail(err);
        if (r.TargetList is not { } list || r.Index < 0)
            return ToolError.Fail($"'{path}' is not a list entry. Right-click the entry itself (e.g. Condition [1]) to remove it.");

        try { list.RemoveAt(r.Index); }
        catch (Exception ex) { return ToolError.Fail($"Could not remove '{path}': {ex.Message}"); }

        var (name, _) = WriteService.SplitPlugin(plugin);
        MutagenLoader.InvalidateModIndex(name); WriteService.RaiseChanged(name);
        return $"Removed {path} from {recordId} in {name} (now {list.Count}). save_plugin to persist.";
    }

    public static string MoveElement(string plugin, string recordId, string path, int delta, object? env)
    {
        var mod = WriteService.GetMutableFor(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var rec = WriteService.FindRecordIn(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!TryResolve(rec, path, initializeNullLists: false, out var r, out var err)) return ToolError.Fail(err);
        if (r.TargetList is not { } list || r.Index < 0)
            return ToolError.Fail($"'{path}' is not a list entry, so it cannot be moved.");

        int to = r.Index + delta;
        if (to < 0 || to >= list.Count) return ToolError.Fail($"'{path}' is already at the {(delta < 0 ? "top" : "bottom")}.");

        var item = list[r.Index];
        list.RemoveAt(r.Index);
        list.Insert(to, item);

        var (name, _) = WriteService.SplitPlugin(plugin);
        MutagenLoader.InvalidateModIndex(name); WriteService.RaiseChanged(name);
        return $"Moved {path} to index {to} on {recordId} in {name}. save_plugin to persist.";
    }

    public static string ClearElement(string plugin, string recordId, string path, object? env)
    {
        var mod = WriteService.GetMutableFor(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var rec = WriteService.FindRecordIn(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!TryResolve(rec, path, initializeNullLists: false, out var r, out var err)) return ToolError.Fail(err);
        if (!r.IsList || r.TargetList is not { } list)
            return ToolError.Fail($"'{path}' is not a list, so there is nothing to clear.");

        int had = list.Count;
        list.Clear();

        var (name, _) = WriteService.SplitPlugin(plugin);
        MutagenLoader.InvalidateModIndex(name); WriteService.RaiseChanged(name);
        return $"Cleared {path} on {recordId} in {name} ({had} item(s) removed). save_plugin to persist.";
    }

    private readonly struct Resolved
    {
        public Resolved(IList? targetList, int index, bool isList) { TargetList = targetList; Index = index; IsList = isList; }
        public IList? TargetList { get; }
        public int Index { get; }
        public bool IsList { get; }
    }

    private static bool TryResolve(
        object record,
        string path,
        bool initializeNullLists,
        out Resolved resolved,
        out string error)
    {
        resolved = default; error = "";
        object? cur = record;
        IList? lastList = null;
        int lastIndex = -1;
        bool endedOnList = false;

        foreach (var (name, index) in SplitPath(path))
        {
            if (cur == null) { error = $"'{path}' does not resolve on {record.GetType().Name}."; return false; }

            var owner = cur;
            var prop = owner.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) { error = $"No field '{name}' on {owner.GetType().Name} (path '{path}')."; return false; }

            cur = prop.GetValue(owner);
            if (cur == null && initializeNullLists && SequenceElementType(prop.PropertyType) != null)
            {
                if (!TryInitializeList(owner, prop, out cur, out var initError))
                {
                    error = $"Could not initialize list field '{name}' on {owner.GetType().Name} (path '{path}'): {initError}";
                    return false;
                }
            }

            if (cur is IList asList) { lastList = asList; lastIndex = -1; endedOnList = true; }
            else endedOnList = false;

            if (index is { } i)
            {
                if (cur is not IList il) { error = $"'{name}' is not a list, so '{name}[{i}]' is meaningless."; return false; }
                if (i < 0 || i >= il.Count) { error = $"'{name}[{i}]' is out of range ({il.Count} item(s))."; return false; }
                lastList = il; lastIndex = i; endedOnList = false;
                cur = il[i];
            }
        }

        resolved = new Resolved(lastList, lastIndex, endedOnList);
        return true;
    }

    private static bool TryInitializeList(object owner, PropertyInfo prop, out object? value, out string error)
    {
        value = null;
        error = "";

        if (!prop.CanWrite)
        {
            error = "the property is read-only.";
            return false;
        }

        var listType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
        var elementType = SequenceElementType(listType);
        if (elementType == null)
        {
            error = $"{listType.Name} is not list-shaped.";
            return false;
        }

        try
        {
            object? instance;
            if (!listType.IsInterface && !listType.IsAbstract)
            {
                instance = Activator.CreateInstance(listType);
            }
            else
            {
                var concrete = typeof(List<>).MakeGenericType(elementType);
                if (!listType.IsAssignableFrom(concrete))
                {
                    error = $"no constructible list type can be assigned to {listType.Name}.";
                    return false;
                }
                instance = Activator.CreateInstance(concrete);
            }

            if (instance is not IList)
            {
                error = $"constructed {instance?.GetType().Name ?? listType.Name} does not implement IList.";
                return false;
            }

            prop.SetValue(owner, instance);
            value = instance;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static IEnumerable<(string name, int? index)> SplitPath(string path)
    {
        foreach (var raw in (path ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var seg = raw.Trim();
            int br = seg.IndexOf('[');
            if (br < 0) { yield return (seg, null); continue; }
            yield return (seg[..br], int.TryParse(seg[(br + 1)..].TrimEnd(']'), out var i) ? i : null);
        }
    }

    private static string ContainerPath(string path)
    {
        int br = path.LastIndexOf('[');
        return br > 0 ? path[..br] : path;
    }

    private static Type ElementTypeOf(IList list)
    {
        foreach (var iface in list.GetType().GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>))
                return iface.GetGenericArguments()[0];
        return typeof(object);
    }

    private static string[] TemplatesFor(Type elemType)
    {
        if (IsFormLink(elemType)) return new[] { FriendlyTypeName(elemType) };
        if (!elemType.IsAbstract && !elemType.IsInterface && elemType.GetConstructor(Type.EmptyTypes) != null)
            return new[] { FriendlyTypeName(elemType) };

        return elemType.Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && elemType.IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(FriendlyTypeName)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
    }

    private static Type? PickTemplate(Type elemType, string? requested, out string error)
    {
        error = "";
        var options = TemplatesFor(elemType);
        if (options.Length == 0)
        {
            error = $"Nothing can be added to a list of {FriendlyTypeName(elemType)} -- it has no constructible type.";
            return null;
        }

        if (IsFormLink(elemType)) return elemType;
        if (!elemType.IsAbstract && !elemType.IsInterface && options.Length == 1) return elemType;

        var want = string.IsNullOrWhiteSpace(requested) ? options[0] : requested!.Trim();
        var match = elemType.Assembly.GetTypes().FirstOrDefault(t =>
            !t.IsAbstract && !t.IsInterface && elemType.IsAssignableFrom(t) &&
            t.GetConstructor(Type.EmptyTypes) != null &&
            string.Equals(FriendlyTypeName(t), want, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            error = $"'{want}' is not one of the types this list accepts: {string.Join(", ", options)}.";
        return match;
    }

    private static bool IsFormLink(Type t)
    {
        if (!t.IsGenericType) return false;
        var g = t.GetGenericTypeDefinition();
        return g == typeof(IFormLink<>) || g == typeof(IFormLinkGetter<>)
            || g == typeof(IFormLinkNullable<>) || g == typeof(IFormLinkNullableGetter<>);
    }

    private static object? CreateInstance(Type t)
    {
        if (IsFormLink(t))
        {
            var target = t.GetGenericArguments()[0];
            var open = t.GetGenericTypeDefinition();
            var concrete = (open == typeof(IFormLinkNullable<>) || open == typeof(IFormLinkNullableGetter<>))
                ? typeof(FormLinkNullable<>) : typeof(FormLink<>);
            return Activator.CreateInstance(concrete.MakeGenericType(target), FormKey.Null);
        }
        return Activator.CreateInstance(t);
    }

    private static string FriendlyTypeName(Type t)
    {
        if (!t.IsGenericType) return t.Name;
        var arg = t.GetGenericArguments()[0];
        var bare = t.Name.Split('`')[0];
        return $"{bare}<{arg.Name}>";
    }

    private static IMajorRecordGetter? FindRecordForRead(object? env, string plugin, string recordId)
    {
        if (!string.IsNullOrWhiteSpace(plugin))
        {
            var (name, _) = WriteService.SplitPlugin(plugin);
            var mutable = WriteService.GetMutable(name);
            if (mutable != null && WriteService.FindRecordIn(mutable, recordId) is { } m) return m;
            if (WriteService.TryResolveFormKey(env, recordId, out var pfk) &&
                MutagenLoader.GetRecordVersion(env, name, pfk) is { } v) return v;
        }
        return WriteService.TryResolveFormKey(env, recordId, out var fk)
            ? MutagenLoader.GetRecordContexts(env, fk).LastOrDefault().rec
            : null;
    }
}
