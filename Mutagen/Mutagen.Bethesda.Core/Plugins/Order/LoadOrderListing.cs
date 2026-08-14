using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Order.DI;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Order;

public interface ILoadOrderListingGetter : IModKeyed
{

    bool Enabled { get; }

    bool Ghosted { get; }

    string GhostSuffix { get; }

    string FileName { get; }

    public static string ToString(ILoadOrderListingGetter getter)
    {
        return $"[{(getter.Enabled ? "X" : "_")}] {getter.ModKey}{(getter.Ghosted ? " (ghosted)" : null)}";
    }
}

[DebuggerDisplay("{ToString()}")]
public sealed record LoadOrderListing : ILoadOrderListingGetter
{

    public ModKey ModKey { get; init; }

    public bool Enabled { get; init; }

    public bool Ghosted => !string.IsNullOrWhiteSpace(GhostSuffix);

    public string GhostSuffix { get; init; } = string.Empty;

    public string FileName => OrderUtility.GetListingFilename(ModKey, GhostSuffix);

    public LoadOrderListing()
    {
    }

    public LoadOrderListing(ModKey modKey, bool enabled, string ghostSuffix = "")
    {
        ModKey = modKey;
        Enabled = enabled;
        GhostSuffix = ghostSuffix;
    }

    public static LoadOrderListing CreateEnabled(ModKey modKey)
    {
        return new LoadOrderListing(modKey, enabled: true, ghostSuffix: "");
    }

    public static LoadOrderListing CreateDisabled(ModKey modKey)
    {
        return new LoadOrderListing(modKey, enabled: false, ghostSuffix: "");
    }

    public static LoadOrderListing CreateGhosted(ModKey modKey, string ghostSuffix)
    {
        return new LoadOrderListing(modKey, enabled: false, ghostSuffix: ghostSuffix);
    }

    public override string ToString()
    {
        return ILoadOrderListingGetter.ToString(this);
    }

    public static implicit operator LoadOrderListing(ModKey modKey)
    {
        return new LoadOrderListing(modKey, enabled: true);
    }

    public static bool TryFromString(ReadOnlySpan<char> str, bool enabledMarkerProcessing, [MaybeNullWhen(false)] out LoadOrderListing listing)
    {
        str = str.Trim();
        bool enabled = true;
        if (enabledMarkerProcessing)
        {
            if (str[0] == '*')
            {
                str = str[1..];
            }
            else
            {
                enabled = false;
            }
        }
        if (ModKey.TryFromNameAndExtension(str, out var key))
        {
            listing = new LoadOrderListing(key, enabled);
            return true;
        }

        var periodIndex = str.LastIndexOf('.');
        if (periodIndex == -1)
        {
            listing = default;
            return false;
        }
        var ghostTerm = str.Slice(periodIndex + 1);
        str = str.Slice(0, periodIndex);

        if (ModKey.TryFromNameAndExtension(str, out key))
        {
            listing = CreateGhosted(key, ghostTerm.ToString());
            return true;
        }

        listing = default;
        return false;
    }

    public static bool TryFromFileName(FileName fileName, bool enabledMarkerProcessing, [MaybeNullWhen(false)] out LoadOrderListing listing)
    {
        return TryFromString(fileName.String, enabledMarkerProcessing, out listing);
    }

    public static LoadOrderListing FromString(ReadOnlySpan<char> str, bool enabledMarkerProcessing)
    {
        if (!TryFromString(str, enabledMarkerProcessing, out var listing))
        {
            throw new InvalidDataException($"Load order file had malformed line: {str.ToString()}");
        }
        return listing;
    }

    public static LoadOrderListing FromFileName(FileName name, bool enabledMarkerProcessing)
    {
        if (!TryFromFileName(name, enabledMarkerProcessing, out var listing))
        {
            throw new InvalidDataException($"Load order file had malformed line: {name}");
        }
        return listing;
    }

    public static Comparer<LoadOrderListing> GetComparer(Comparer<ModKey> comparer) =>
        Comparer<LoadOrderListing>.Create((x, y) => comparer.Compare(x.ModKey, y.ModKey));

    public static Comparer<TListing> GetComparer<TListing>(Comparer<ModKey> comparer)
        where TListing : ILoadOrderListingGetter
    {
        return Comparer<TListing>.Create((x, y) => comparer.Compare(x.ModKey, y.ModKey));
    }
}
