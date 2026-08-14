using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using FO4RecordEditor.Services.Materials;
using Newtonsoft.Json;

namespace FO4RecordEditor.Services;

public static class MaterialService
{
    public static string Inspect(string path)
    {
        var (data, err) = LoadMaterial(path);
        if (data == null) return err;

        var sb = new StringBuilder();
        sb.AppendLine($"{Path.GetFileName(path)} ({MaterialCodec.FormatName(data)} v{data.Header.Version}):");
        sb.AppendLine("-- Header --");
        AppendFields(sb, data.Header);
        sb.AppendLine("-- Material --");
        AppendFields(sb, data);
        return sb.ToString().TrimEnd();
    }

    public static string SetField(string path, string field, string value, string? outPath)
    {
        var (data, err) = LoadMaterial(path);
        if (data == null) return err;
        if (string.IsNullOrWhiteSpace(field))
            return ToolError.Fail("Provide a field name -- call bgsm_inspect first to see the exact names for this file.");

        var (target, prop) = ResolveField(data, field);
        if (prop == null)
            return ToolError.Fail($"Unknown {MaterialCodec.FormatName(data)} field '{field}'. Call bgsm_inspect to see this file's exact field names and current values.");

        object? parsed;
        try { parsed = ParseValue(prop.PropertyType, value); }
        catch (Exception ex) { return ToolError.Fail($"Could not parse '{value}' as {FriendlyTypeName(prop.PropertyType)} for '{field}': {ex.Message}"); }

        prop.SetValue(target, parsed);

        byte[] written;
        try { written = MaterialCodec.Write(data); }
        catch (Exception ex) { return ToolError.Fail($"Re-encoding failed after setting '{field}': {ex.Message}"); }

        var dest = string.IsNullOrWhiteSpace(outPath) ? path : outPath;
        try
        {
            var fullDest = Path.GetFullPath(dest);
            Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
            File.WriteAllBytes(fullDest, written);
        }
        catch (Exception ex) { return ToolError.Fail($"Write failed: {ex.Message}"); }

        return $"Set {field} = {value} in '{Path.GetFileName(path)}', wrote to '{dest}'.";
    }

    public static string InspectJson(string path)
    {
        var (data, err) = LoadMaterial(path);
        if (data == null) return JsonConvert.SerializeObject(new { error = err });

        var fields = new List<object>();
        CollectFields(fields, "material", data);
        CollectFields(fields, "header", data.Header);

        return JsonConvert.SerializeObject(new
        {
            fileName = Path.GetFileName(path),
            format = MaterialCodec.FormatName(data),
            version = data.Header.Version,
            fields,
        });
    }

    public static string SetFields(string path, Dictionary<string, string> fields, string? outPath)
    {
        var (data, err) = LoadMaterial(path);
        if (data == null) return err;
        if (fields == null || fields.Count == 0) return ToolError.Fail("No field changes to apply.");

        var applied = new List<string>();
        foreach (var (field, value) in fields)
        {
            var (target, prop) = ResolveField(data, field);
            if (prop == null) return ToolError.Fail($"Unknown {MaterialCodec.FormatName(data)} field '{field}'.");

            object? parsed;
            try { parsed = ParseValue(prop.PropertyType, value); }
            catch (Exception ex) { return ToolError.Fail($"Could not parse '{value}' as {FriendlyTypeName(prop.PropertyType)} for '{field}': {ex.Message}"); }

            prop.SetValue(target, parsed);
            applied.Add(field);
        }

        byte[] written;
        try { written = MaterialCodec.Write(data); }
        catch (Exception ex) { return ToolError.Fail($"Re-encoding failed: {ex.Message}"); }

        var dest = string.IsNullOrWhiteSpace(outPath) ? path : outPath;
        try
        {
            var fullDest = Path.GetFullPath(dest);
            Directory.CreateDirectory(Path.GetDirectoryName(fullDest)!);
            File.WriteAllBytes(fullDest, written);
        }
        catch (Exception ex) { return ToolError.Fail($"Write failed: {ex.Message}"); }

        return $"Set {applied.Count} field(s) ({string.Join(", ", applied)}) in '{Path.GetFileName(path)}', wrote to '{dest}'.";
    }

    private static void CollectFields(List<object> list, string section, object obj)
    {
        foreach (var prop in obj.GetType().GetProperties())
        {
            if (prop.Name == nameof(IMaterialData.Header)) continue;
            var val = prop.GetValue(obj);
            if (val == null) continue;
            list.Add(new
            {
                name = prop.Name,
                section,
                type = FieldTypeName(prop.PropertyType),
                value = FormatValue(val),
            });
        }
    }

    private static string FieldTypeName(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(bool)) return "bool";
        if (u == typeof(float)) return "float";
        if (u == typeof(uint) || u == typeof(byte)) return "int";
        if (u == typeof(float[])) return "color";
        return "string";
    }

    private static (IMaterialData? data, string error) LoadMaterial(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return (null, "Provide a .bgsm or .bgem file path.");
        if (!File.Exists(path)) return (null, ToolError.Fail($"File not found: '{path}'."));

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception ex) { return (null, ToolError.Fail($"Could not read '{path}': {ex.Message}")); }

        try { return (MaterialCodec.Parse(bytes), ""); }
        catch (Exception ex) { return (null, ToolError.Fail($"Could not parse '{path}' as a material: {ex.Message}")); }
    }

    private static void AppendFields(StringBuilder sb, object obj)
    {
        foreach (var prop in obj.GetType().GetProperties())
        {
            var val = prop.GetValue(obj);
            if (val == null) continue;
            sb.AppendLine($"  {prop.Name} = {FormatValue(val)}");
        }
    }

    private static string FormatValue(object val) => val switch
    {
        float[] arr => string.Join(", ", arr.Select(f => f.ToString("0.###", CultureInfo.InvariantCulture))),
        string s => s.TrimEnd('\0'),
        bool b => b ? "true" : "false",
        float f => f.ToString("0.###", CultureInfo.InvariantCulture),
        _ => val.ToString() ?? "",
    };

    private static (object target, PropertyInfo? prop) ResolveField(IMaterialData data, string field)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        var bodyProp = data.GetType().GetProperty(field, flags);
        if (bodyProp != null && bodyProp.Name != nameof(IMaterialData.Header)) return (data, bodyProp);
        var headerProp = typeof(MaterialHeader).GetProperty(field, flags);
        if (headerProp != null) return (data.Header, headerProp);
        return (data, null);
    }

    private static object? ParseValue(Type propType, string value)
    {
        var underlying = Nullable.GetUnderlyingType(propType) ?? propType;
        if (underlying == typeof(bool)) return bool.Parse(value);
        if (underlying == typeof(float)) return float.Parse(value, CultureInfo.InvariantCulture);
        if (underlying == typeof(uint)) return uint.Parse(value, CultureInfo.InvariantCulture);
        if (underlying == typeof(byte)) return byte.Parse(value, CultureInfo.InvariantCulture);
        if (underlying == typeof(string)) return value;
        if (underlying == typeof(float[]))
        {
            var parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) throw new FormatException("expected 3 comma/space-separated numbers, e.g. '1.0, 0.5, 0.2'");
            return parts.Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
        }
        throw new NotSupportedException($"Unsupported field type {propType.Name}.");
    }

    private static string FriendlyTypeName(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        if (u == typeof(float[])) return "a color (3 numbers)";
        return u.Name.ToLowerInvariant();
    }
}
