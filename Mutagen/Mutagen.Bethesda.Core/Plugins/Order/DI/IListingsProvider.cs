namespace Mutagen.Bethesda.Plugins.Order.DI;

public interface IListingsProvider
{






    public IEnumerable<ILoadOrderListingGetter> Get();
}