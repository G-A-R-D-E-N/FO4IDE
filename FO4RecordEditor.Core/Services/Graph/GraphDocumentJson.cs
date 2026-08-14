using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace FO4RecordEditor.Services.Graph;

public static class GraphDocumentJson
{

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
