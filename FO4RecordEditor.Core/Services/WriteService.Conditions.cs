using System.Text.Json;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;

namespace FO4RecordEditor.Services;

/// <summary>
/// The read half of condition editing. <see cref="WriteService.SetConditions"/> replaces the whole
/// list, so anything that wants to add or change ONE condition (the right-click "Add condition"
/// path in the grid, or an AI editing an existing rule) first has to read the current list back in
/// the exact JSON shape SetConditions accepts. That round trip is what this file provides.
/// </summary>
public static partial class WriteService
{
    /// <summary>
    /// The record's Conditions list as a JSON array in set_conditions' own schema, so it can be
    /// edited and handed straight back. Reads the given plugin's version of the record (the
    /// in-editor copy when one is open), or the winning version when plugin is empty.
    /// Returns "[]" for a record that has a Conditions list but no entries.
    /// </summary>
    public static string GetConditionsJson(object? env, string plugin, string recordId)
    {
        IMajorRecordGetter? rec = null;

        if (!string.IsNullOrWhiteSpace(plugin))
        {
            var (name, _) = NormalizePlugin(plugin);
            var mutable = GetMutable(name);
            if (mutable != null) rec = FindMutableRecord(mutable, recordId);
            if (rec == null && ResolveFk(env, recordId, out var pfk))
                rec = MutagenLoader.GetRecordVersion(env, name, pfk);
        }
        if (rec == null && ResolveFk(env, recordId, out var fk))
            rec = MutagenLoader.GetRecordContexts(env, fk).LastOrDefault().rec;

        if (rec == null) return ToolError.Fail($"Record '{recordId}' not found.");

        var prop = rec.GetType().GetProperty("Conditions");
        if (prop?.GetValue(rec) is not System.Collections.IEnumerable list)
            return ToolError.Fail($"{rec.Registration.Name} has no Conditions list.");

        var outp = new List<Dictionary<string, object?>>();
        foreach (var item in list)
        {
            if (item is not IConditionGetter cond) continue;
            outp.Add(DescribeCondition(cond));
        }
        return JsonSerializer.Serialize(outp);
    }

    /// <summary>Every condition function name the editor accepts, for a picker. Sorted.</summary>
    public static string ConditionFunctionNames() =>
        JsonSerializer.Serialize(Enum.GetNames<Condition.Function>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray());

    /// <summary>Run-on targets a condition can evaluate against, for a picker.</summary>
    public static string ConditionRunOnNames() =>
        JsonSerializer.Serialize(Enum.GetNames<Condition.RunOnType>());

    private static Dictionary<string, object?> DescribeCondition(IConditionGetter cond)
    {
        var d = new Dictionary<string, object?>();
        var data = cond.Data;

        d["function"] = data is IFunctionConditionDataGetter fc ? fc.Function.ToString() : data.GetType().Name;
        d["operator"] = OperatorToString(cond.CompareOperator);

        if (data is IFunctionConditionDataGetter fcd)
        {
            if (!fcd.ParameterOneRecord.FormKey.IsNull) d["param1"] = fcd.ParameterOneRecord.FormKey.ToString();
            else if (fcd.ParameterOneNumber != 0) d["param1"] = fcd.ParameterOneNumber;
            if (!fcd.ParameterTwoRecord.FormKey.IsNull) d["param2"] = fcd.ParameterTwoRecord.FormKey.ToString();
            else if (fcd.ParameterTwoNumber != 0) d["param2"] = fcd.ParameterTwoNumber;
        }

        switch (cond)
        {
            case IConditionGlobalGetter g: d["compareGlobal"] = g.ComparisonValue.FormKey.ToString(); break;
            case IConditionFloatGetter f: d["value"] = f.ComparisonValue; break;
        }

        d["runOn"] = data.RunOnType.ToString();
        if (!data.Reference.FormKey.IsNull) d["reference"] = data.Reference.FormKey.ToString();
        if (cond.Flags != default) d["flags"] = cond.Flags.ToString();
        return d;
    }

    // The inverse of ParseOperator, so a read/edit/write round trip does not silently rewrite the
    // operator into its enum spelling.
    private static string OperatorToString(CompareOperator op) => op switch
    {
        CompareOperator.EqualTo => "==",
        CompareOperator.NotEqualTo => "!=",
        CompareOperator.GreaterThan => ">",
        CompareOperator.GreaterThanOrEqualTo => ">=",
        CompareOperator.LessThan => "<",
        CompareOperator.LessThanOrEqualTo => "<=",
        _ => "==",
    };
}
