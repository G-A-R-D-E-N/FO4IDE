using System.Diagnostics.CodeAnalysis;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Order.DI;




public interface ILoadOrderListingParser
{






    bool TryFromString(ReadOnlySpan<char> str, [MaybeNullWhen(false)] out LoadOrderListing listing);







    bool TryFromFileName(FileName fileName, [MaybeNullWhen(false)] out LoadOrderListing listing);







    LoadOrderListing FromString(ReadOnlySpan<char> str);







    LoadOrderListing FromFileName(FileName fileName);
}

public sealed class LoadOrderListingParser : ILoadOrderListingParser
{
    private readonly IHasEnabledMarkersProvider _hasEnabledMarkers;

    public LoadOrderListingParser(IHasEnabledMarkersProvider hasEnabledMarkers)
    {
        _hasEnabledMarkers = hasEnabledMarkers;
    }


    public bool TryFromString(ReadOnlySpan<char> str, [MaybeNullWhen(false)] out LoadOrderListing listing)
    {
        return LoadOrderListing.TryFromString(str, _hasEnabledMarkers.HasEnabledMarkers, out listing);
    }


    public bool TryFromFileName(FileName fileName, [MaybeNullWhen(false)] out LoadOrderListing listing)
    {
        return LoadOrderListing.TryFromFileName(fileName, _hasEnabledMarkers.HasEnabledMarkers, out listing);
    }


    public LoadOrderListing FromString(ReadOnlySpan<char> str)
    {
        return LoadOrderListing.FromString(str, _hasEnabledMarkers.HasEnabledMarkers);
    }


    public LoadOrderListing FromFileName(FileName name)
    {
        return LoadOrderListing.FromFileName(name, _hasEnabledMarkers.HasEnabledMarkers);
    }
}