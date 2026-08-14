using Mutagen.Bethesda.Plugins.Binary.Translations;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

public sealed class RecordTypeConverter
{

    public Dictionary<RecordType, RecordType> FromConversions = new Dictionary<RecordType, RecordType>();

    public Dictionary<RecordType, RecordType> ToConversions = new Dictionary<RecordType, RecordType>();

    public RecordTypeConverter(params KeyValuePair<RecordType, RecordType>[] conversions)
    {
        foreach (var conv in conversions)
        {
            FromConversions[conv.Key] = conv.Value;
            ToConversions[conv.Value] = conv.Key;
        }
    }
}

public static class RecordTypeConverterExt
{

    public static RecordType ConvertToCustom(this RecordTypeConverter? converter, RecordType rec)
    {
        if (converter == null) return rec;
        if (converter.FromConversions.TryGetValue(rec, out var converted))
        {
            rec = converted;
        }
        else if (converter.ToConversions.ContainsKey(rec))
        {
            return RecordType.Null;
        }
        return rec;
    }

    public static RecordType ConvertToStandard(this RecordTypeConverter? converter, RecordType rec)
    {
        if (converter == null) return rec;
        if (converter.ToConversions.TryGetValue(rec, out var converted))
        {
            rec = converted;
        }
        else if (converter.FromConversions.ContainsKey(rec))
        {
            return RecordType.Null;
        }
        return rec;
    }

    public static RecordTypeConverter? Combine(this RecordTypeConverter? lhs, RecordTypeConverter? rhs)
    {
        if (lhs == null) return rhs;
        if (rhs == null) return null;
        throw new NotImplementedException();
    }

    public static TypedParseParams DoNotShortCircuit(this RecordTypeConverter? lhs)
    {
        TypedParseParams typedParseParams = lhs;
        return typedParseParams.DoNotShortCircuit();
    }
}