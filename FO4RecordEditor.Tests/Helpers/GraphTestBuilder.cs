using FO4RecordEditor.Models;

namespace FO4RecordEditor.Tests.Helpers;

public static class GraphTestBuilder
{
    public static RecordNode Plugin(string fileName, params RecordNode[] records)
    {
        var root = new RecordNode { Key = fileName, FilePath = $"C:\\fake\\{fileName}" };
        foreach (var rec in records)
        {
            var group = root.GetChild(rec.GetValue("Type") ?? "MISC")
                        ?? AddGroup(root, rec.GetValue("Type") ?? "MISC");
            rec.Parent = group;
            group.Children.Add(rec);
        }
        return root;
    }

    private static RecordNode AddGroup(RecordNode root, string sig)
    {
        var g = new RecordNode { Key = sig, Parent = root };
        root.Children.Add(g);
        return g;
    }

    public static RecordNode Record(string type, string formKey, string editorId,
        params (string key, string val)[] fields)
    {
        var rec = new RecordNode { Key = editorId };
        AddLeaf(rec, "Type", type);
        AddLeaf(rec, "FormKey", formKey);
        AddLeaf(rec, "EditorID", editorId);
        foreach (var (k, v) in fields) AddLeaf(rec, k, v);
        return rec;
    }

    public static void AddLeaf(RecordNode parent, string key, string value)
    {
        var n = new RecordNode { Key = key, Parent = parent };
        n.Value = value;
        parent.Children.Add(n);
    }
}
