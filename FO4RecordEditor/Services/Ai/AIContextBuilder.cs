using System.Text;
using FO4RecordEditor.Models;

namespace FO4RecordEditor.Services;

public sealed class AIContextBuilder
{
    private readonly KnowledgeGraph _graph;
    public AIContextBuilder(KnowledgeGraph graph) => _graph = graph;

    public const string SystemPreamble =
        "You are an expert Bethesda (Fallout 4) plugin modding assistant embedded in a " +
        "record editor. You understand ESP/ESM/ESL records, FormKeys (format ID:Plugin.esp), " +
        "keywords, leveled lists, OMODs, conditions (CTDA), and crafting (COBJ). When asked to " +
        "modify records, respond with a concrete change plan the user can approve. Be concise.";

    // Cap on field lines emitted per record so a wide record (e.g. a leveled list with
    // hundreds of entries) cannot balloon the prompt. Depth is already capped at 3.
    private const int MaxFieldLines = 200;

    public string BuildForRecord(RecordEntry rec)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SystemPreamble);
        sb.AppendLine();
        AppendNodeContext(sb, rec.Node);
        return sb.ToString();
    }

    public string BuildForQuestion(string question, RecordNode? selected, IReadOnlyList<string>? loadedPlugins = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SystemPreamble);   // preamble emitted exactly once

        // Inject the current plugin list so Claude never has to call list_plugins just to discover
        // what's loaded. This is the single most important piece of ambient context.
        sb.AppendLine();
        if (loadedPlugins != null && loadedPlugins.Count > 0)
        {
            sb.AppendLine("## Loaded plugins (in load order -- these are what you can read and edit):");
            foreach (var p in loadedPlugins) sb.AppendLine($"- {p}");
        }
        else
        {
            sb.AppendLine("## Loaded plugins: none loaded yet. Ask the user to open a modlist or individual plugin.");
        }

        if (selected != null)
        {
            sb.AppendLine();
            AppendNodeContext(sb, selected);
        }
        sb.AppendLine($"\n## Graph summary: {_graph.RecordCount} records, {_graph.ReferenceCount} references.");
        return sb.ToString();
    }

    private void AppendNodeContext(StringBuilder sb, RecordNode node)
    {
        var fk = node.GetValue("FormKey");
        var eid = node.GetValue("EditorID");
        var type = node.GetValue("Type") ?? "Group/Plugin";
        
        if (fk != null)
        {
            sb.AppendLine($"## Selected record: {eid} ({type}) [{fk}]");
            AppendSchema(sb, type);
        }
        else
        {
            sb.AppendLine($"## Selected node: {node.Key} ({type})");
        }

        int lines = 0;
        AppendFields(sb, node, 0, maxDepth: 3, ref lines);

        if (fk != null)
        {
            var hood = _graph.GetNeighborhood(fk).Where(e => e.FormKey != fk).ToList();
            if (hood.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Linked records (one hop):");
                foreach (var n in hood.Take(20))
                    sb.AppendLine($"- {n.EditorID} ({n.Type}) [{n.FormKey}]");
            }

            var inbound = _graph.GetReferencesTo(fk);
            if (inbound.Count > 0)
            {
                sb.AppendLine("\n## Referenced By (Incoming Links):");
                foreach (var r in inbound.Take(15))
                {
                    var source = _graph.GetByFormKey(r.FromFormKey);
                    var sourceDesc = source != null ? $"{source.EditorID} ({source.Type})" : r.FromFormKey;
                    sb.AppendLine($"- {sourceDesc} uses this record as {r.FieldPath} ({r.Kind})");
                }
                if (inbound.Count > 15) sb.AppendLine($"- ... and {inbound.Count - 15} more.");
                sb.AppendLine($"Warning: modifying this record may affect the {inbound.Count} records listed above.");
            }
        }
    }

    private void AppendSchema(StringBuilder sb, string type)
    {
        var schema = GetSchemaForType(type);
        if (schema != null)
        {
            sb.AppendLine("\n## Mutagen API Schema for this record type:");
            sb.AppendLine("```csharp");
            sb.AppendLine(schema);
            sb.AppendLine("```");
        }
    }

    private string? GetSchemaForType(string type) => type.ToUpperInvariant() switch
    {
        "ARMO" => "public interface IArmorGetter : IMajorRecordGetter { \n  IFormLinkGetter<IObjectBoundsGetter> ObjectBounds { get; }\n  uint? Value { get; }\n  float? Weight { get; }\n  IFormLinkGetter<IRaceGetter> Race { get; }\n  IReadOnlyList<IArmorRatingGetter> ArmorRating { get; }\n  IFormLinkGetter<IKeywordsGetter> Keywords { get; }\n}",
        "WEAP" => "public interface IWeaponGetter : IMajorRecordGetter { \n  uint? Value { get; }\n  float? Weight { get; }\n  ushort? AmmoCount { get; }\n  IFormLinkGetter<IAmmoGetter> Ammo { get; }\n  IFormLinkGetter<IKeywordsGetter> Keywords { get; }\n  float? SightFOV { get; }\n}",
        "NPC_" => "public interface INpcGetter : IMajorRecordGetter { \n  IFormLinkGetter<IRaceGetter> Race { get; }\n  IFormLinkGetter<IClassGetter> Class { get; }\n  short? Level { get; }\n  IReadOnlyList<IFormLinkGetter<IFactionGetter>> Factions { get; }\n}",
        _ => null
    };

    private static void AppendFields(StringBuilder sb, RecordNode node, int depth, int maxDepth, ref int lines)
    {
        if (depth > maxDepth) return;
        foreach (var c in node.Children)
        {
            if (lines >= MaxFieldLines) { sb.AppendLine("  ... (fields truncated)"); return; }
            lines++;
            var indent = new string(' ', depth * 2);
            
            if (c.IsLeaf)
            {
                if (c.Values.Count > 1)
                {
                    sb.AppendLine($"{indent}{c.Key} (Conflict):");
                    foreach (var kv in c.Values)
                        sb.AppendLine($"{indent}  - {kv.Key}: {kv.Value}");
                    sb.AppendLine($"{indent}  => Winning value: {c.Value}");
                }
                else
                {
                    sb.AppendLine($"{indent}{c.Key}: {c.Value}");
                }
            }
            else 
            { 
                sb.AppendLine($"{indent}{c.Key}:"); 
                AppendFields(sb, c, depth + 1, maxDepth, ref lines); 
            }
        }
    }
}
