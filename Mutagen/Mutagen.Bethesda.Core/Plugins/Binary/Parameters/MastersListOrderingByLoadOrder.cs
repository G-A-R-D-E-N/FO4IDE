using Mutagen.Bethesda.Plugins.Order;

namespace Mutagen.Bethesda.Plugins.Binary.Parameters;




public class MastersListOrderingByLoadOrder : AMastersListOrderingOption
{
    private readonly List<ModKey> _modKeys;

    public IReadOnlyList<ModKey> LoadOrder => _modKeys;




    public bool Strict { get; set; }

    public MastersListOrderingByLoadOrder(IEnumerable<ModKey> modKeys)
    {
        _modKeys = modKeys.ToList();
    }

    public MastersListOrderingByLoadOrder(ILoadOrderGetter lo)
        : this(lo.ListedOrder)
    {
    }

    public static MastersListOrderingByLoadOrder Factory(IEnumerable<ModKey> modKeys) => new MastersListOrderingByLoadOrder(modKeys);

    public static MastersListOrderingByLoadOrder Factory<T>(LoadOrder<T> loadOrder)
        where T : IModKeyed
    {
        return Factory(loadOrder.Select(listing => listing.Key));
    }
}