using System.Collections;
using System.Reflection;
using System.Text.Json;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Services;

/// <summary>
/// xEdit's element menu, as it actually behaves. Read from
/// <c>xeMainForm.pas: pmuViewPopup / mniViewAddClick / mniViewRemoveClick / mniViewMoveUpClick</c>:
///
/// - <b>Add never prompts.</b> It calls <c>TargetElement.Assign(TargetIndex, template, False)</c>,
///   which constructs a new element of the container's own type, then
///   <c>SetToDefaultIfAsCreatedEmpty</c>, then focuses it for inline editing. Asking the user to
///   type a FormKey up front is not what it does.
/// - <b>Add targets the parent container and inserts at the clicked row's index</b>, because
///   <c>GetAddElement</c> walks UP from the focused node until it finds an element, keeping the node
///   index as the insert position. Right-clicking <c>Condition [2]</c> inserts a new condition at 2.
/// - <b>Add becomes a submenu when the container accepts more than one type</b>
///   (<c>GetAssignTemplates</c>), and reads <c>Add "Name"</c> when there is exactly one. Our
///   equivalent is the concrete subclasses of an abstract element type: a Conditions list holds the
///   abstract <c>Condition</c>, whose real members are ConditionFloat and ConditionGlobal.
/// - Remove / Move up / Move down / Clear are each shown only when the element supports them
///   (<c>IsRemovable</c>, <c>CanMoveUp</c>, <c>CanMoveDown</c>, <c>IsClearable</c>).
///
/// Not covered here, because they depend on xEdit's multi-record selection and sibling compare,
/// which this editor does not have: Copy to selected records, Remove from selected, Compare
/// referenced row, Sort, Stick, and member switching for unions.
/// </summary>
public static class ElementService
{
    /// <summary>
    /// What the element menu should offer for one grid row, so the UI shows only legal actions.
    ///
    /// Capability is read from the record's concrete SETTER type, not from the instance in front of
    /// us: a record loaded from disk is a binary overlay whose list properties surface as
    /// <c>IReadOnlyList</c>, so asking the instance "are you a list I can add to" answers no for
    /// every unopened plugin. Counts still come from the instance, because only it knows them.
    /// </summary>
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
        public Type? ElementType;   // element type of the list the path lands in or on
        public int Index;           // index within it when the path names an entry, else -1
        public int Count;           // how many entries that list currently holds
        public bool IsList;         // the final segment is the list itself
    }

    // Walk the path over the concrete setter TYPE (for capability) and the getter INSTANCE (for
    // counts) at the same time, so an unopened, overlay-backed record still describes correctly.
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
                // An abstract element type tells us nothing about the entry we actually landed on;
                // the instance does, and the path continues from there.
                if (curVal != null) curType = curVal.GetType();
            }
        }
        return true;
    }

    // The element type of any sequence property (IList<T>, IReadOnlyList<T>, ExtendedList<T>).
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

    /// <summary>
    /// xEdit "Add": construct a new element of the container's type and insert it at the clicked
    /// row's position (append when the row IS the container). No value is asked for -- the new
    /// element is default-constructed and then edited in the grid.
    /// </summary>
    public static string AddElement(string plugin, string recordId, string path, string? template, object? env)
    {
        var mod = WriteService.GetMutableFor(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var rec = WriteService.FindRecordIn(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!TryResolve(rec, path, out var r, out var err)) return ToolError.Fail(err);
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

    /// <summary>xEdit "Remove": drop the element the path names. The path must name an entry, not the list.</summary>
    public static string RemoveElement(string plugin, string recordId, string path, object? env)
    {
        var mod = WriteService.GetMutableFor(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var rec = WriteService.FindRecordIn(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!TryResolve(rec, path, out var r, out var err)) return ToolError.Fail(err);
        if (r.TargetList is not { } list || r.Index < 0)
            return ToolError.Fail($"'{path}' is not a list entry. Right-click the entry itself (e.g. Condition [1]) to remove it.");

        try { list.RemoveAt(r.Index); }
        catch (Exception ex) { return ToolError.Fail($"Could not remove '{path}': {ex.Message}"); }

        var (name, _) = WriteService.SplitPlugin(plugin);
        MutagenLoader.InvalidateModIndex(name); WriteService.RaiseChanged(name);
        return $"Removed {path} from {recordId} in {name} (now {list.Count}). save_plugin to persist.";
    }

    /// <summary>xEdit "Move up" / "Move down": reorder within the list. Order is meaningful for
    /// conditions, leveled entries and effects, so this is a real edit, not cosmetic.</summary>
    public static string MoveElement(string plugin, string recordId, string path, int delta, object? env)
    {
        var mod = WriteService.GetMutableFor(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var rec = WriteService.FindRecordIn(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!TryResolve(rec, path, out var r, out var err)) return ToolError.Fail(err);
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

    /// <summary>xEdit "Clear": empty the list the path names, without removing the field itself.</summary>
    public static string ClearElement(string plugin, string recordId, string path, object? env)
    {
        var mod = WriteService.GetMutableFor(plugin, env, out var openMsg);
        if (mod == null) return ToolError.Fail(openMsg);
        var rec = WriteService.FindRecordIn(mod, recordId);
        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found in {plugin}.");

        if (!TryResolve(rec, path, out var r, out var err)) return ToolError.Fail(err);
        if (!r.IsList || r.TargetList is not { } list)
            return ToolError.Fail($"'{path}' is not a list, so there is nothing to clear.");

        int had = list.Count;
        list.Clear();

        var (name, _) = WriteService.SplitPlugin(plugin);
        MutagenLoader.InvalidateModIndex(name); WriteService.RaiseChanged(name);
        return $"Cleared {path} on {recordId} in {name} ({had} item(s) removed). save_plugin to persist.";
    }

    // ── path resolution ─────────────────────────────────────────────────────────────────────────

    private readonly struct Resolved
    {
        public Resolved(IList? targetList, int index, bool isList) { TargetList = targetList; Index = index; IsList = isList; }
        /// <summary>The list the path lands in or on. Null when the path names no list at all.</summary>
        public IList? TargetList { get; }
        /// <summary>Index within TargetList when the path names an ENTRY; -1 when it names the list.</summary>
        public int Index { get; }
        /// <summary>True when the final segment is the list itself rather than one of its entries.</summary>
        public bool IsList { get; }
    }

    // Walk "Effects[0].Conditions[1]" from the record. Mirrors GetAddElement's walk: the last list
    // seen is the container, and the last index is where the click landed inside it.
    private static bool TryResolve(object record, string path, out Resolved resolved, out string error)
    {
        resolved = default; error = "";
        object? cur = record;
        IList? lastList = null;
        int lastIndex = -1;
        bool endedOnList = false;

        foreach (var (name, index) in SplitPath(path))
        {
            if (cur == null) { error = $"'{path}' does not resolve on {record.GetType().Name}."; return false; }

            var prop = cur.GetType().GetProperty(name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) { error = $"No field '{name}' on {cur.GetType().Name} (path '{path}')."; return false; }
            cur = prop.GetValue(cur);

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

    // ── element construction ────────────────────────────────────────────────────────────────────

    private static Type ElementTypeOf(IList list)
    {
        foreach (var iface in list.GetType().GetInterfaces())
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IList<>))
                return iface.GetGenericArguments()[0];
        return typeof(object);
    }

    /// <summary>
    /// xEdit's GetAssignTemplates, in our terms. A list of a concrete type has exactly one template.
    /// A list of an abstract type or interface has one per usable implementation -- a Conditions
    /// list holds the abstract Condition, whose members are ConditionFloat and ConditionGlobal, and
    /// that is the case where xEdit shows a submenu instead of a single "Add".
    /// </summary>
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

    // A FormLink list element is an interface, so it cannot be constructed directly; a null link is
    // the right empty value, and the grid's record picker fills it in -- the same two steps xEdit
    // takes (add an empty entry, then edit it).
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

    // ── record lookup ───────────────────────────────────────────────────────────────────────────

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
