using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Strings;

public interface IStringsLookup : IEnumerable<KeyValuePair<uint, string>>
{

    bool TryLookup(uint key, [MaybeNullWhen(false)] out string str);

    string? Lookup(uint key)
    {
        if (TryLookup(key, out var str))
        {
            return str;
        }
        return null;
    }
}