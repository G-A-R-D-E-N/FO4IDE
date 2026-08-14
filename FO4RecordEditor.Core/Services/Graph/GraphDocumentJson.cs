using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace FO4RecordEditor.Services.Graph;

/// <summary>
/// Reading and writing graph documents.
/// </summary>
/// <remarks>
/// camelCase, matching what <c>PapyrusAnalysisService</c> already sends the canvas, so the whole
/// bridge speaks one dialect. That casing is the single most likely place the C# and TypeScript
/// halves drift, so it is set once here rather than per call site.
/// <para>
/// Deserialization is forward tolerant. A document written by a newer canvas carrying a node kind
/// this build does not know loads with that node marked <see cref="GraphNodeKind.Unknown"/> rather
/// than throwing, so an older build can still open, render and refuse a newer graph with a message
/// that says why.
/// </para>
/// </remarks>
public static class GraphDocumentJson
{
    /// <summary>
    /// Writes enums as camelCase names and reads an unrecognised one as the enum's zero value.
    /// </summary>
    /// <remarks>
    /// The stock string converter throws on a name it does not know, which would make a document
    /// written by a newer canvas unopenable. Every enum in the document model reserves its zero
    /// value for exactly this: <see cref="GraphNodeKind.Unknown"/> renders and is refused by name,
    /// which is a far better outcome than a canvas that will not open the file at all.
    /// </remarks>
    private sealed class TolerantStringEnumConverter : StringEnumConverter
    {
        public TolerantStringEnumConverter() : base(new CamelCaseNamingStrategy()) { }

        public override object? ReadJson(
            JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            try
            {
                return base.ReadJson(reader, objectType, existingValue, serializer);
            }
            catch (JsonSerializationException)
            {
                var underlying = Nullable.GetUnderlyingType(objectType) ?? objectType;
                return underlying.IsEnum ? Enum.ToObject(underlying, 0) : null;
            }
        }
    }

    private static JsonSerializerSettings Build(Formatting formatting) => new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        Formatting = formatting,
        Converters = { new TolerantStringEnumConverter() },
    };

    public static JsonSerializerSettings Settings { get; } = Build(Formatting.Indented);

    private static readonly JsonSerializerSettings Compact = Build(Formatting.None);

    public static string Serialize(GraphDocument document, bool indented = true) =>
        JsonConvert.SerializeObject(document, indented ? Settings : Compact);

    /// <summary>
    /// Reads a document, never throwing.
    /// </summary>
    /// <remarks>
    /// A malformed document is a diagnostic, not an exception. The canvas calls this on every open
    /// and on every autosave restore, and a throw there would surface as a blank panel with no
    /// explanation.
    /// </remarks>
    public static bool TryDeserialize(string? json, out GraphDocument? document, out GraphDiagnostic? error)
    {
        document = null;
        error = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = GraphDiagnostic.Error(
                GraphDiagnosticCodes.MalformedDocument, "The graph document is empty.");
            return false;
        }

        try
        {
            document = JsonConvert.DeserializeObject<GraphDocument>(json, Settings);
        }
        catch (JsonException ex)
        {
            error = GraphDiagnostic.Error(
                GraphDiagnosticCodes.MalformedDocument, $"The graph document could not be read: {ex.Message}");
            return false;
        }

        if (document == null)
        {
            error = GraphDiagnostic.Error(
                GraphDiagnosticCodes.MalformedDocument, "The graph document contained no object.");
            return false;
        }

        if (document.Schema > GraphDocument.CurrentSchema)
        {
            error = GraphDiagnostic.Error(
                GraphDiagnosticCodes.UnsupportedSchema,
                $"This graph was written at schema {document.Schema}, and this build reads up to "
                + $"{GraphDocument.CurrentSchema}. Update the tool to open it.");
            document = null;
            return false;
        }

        document.Invalidate();
        return true;
    }

    public static GraphDocument Deserialize(string json) =>
        TryDeserialize(json, out var document, out var error)
            ? document!
            : throw new JsonException(error!.Message);

    public static GraphDocument LoadFile(string path) => Deserialize(File.ReadAllText(path));

    public static void SaveFile(GraphDocument document, string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, Serialize(document));
    }
}
