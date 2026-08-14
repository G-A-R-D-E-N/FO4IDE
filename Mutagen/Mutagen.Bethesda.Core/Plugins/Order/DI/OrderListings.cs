using Noggog;

namespace Mutagen.Bethesda.Plugins.Order.DI;




public interface IOrderListings
{






    IEnumerable<T> Order<T>(IEnumerable<T> e, Func<T, ModKey> selector);









    IEnumerable<T> Order<T>(
        IEnumerable<T> implicitListings,
        IEnumerable<T> pluginsListings,
        IEnumerable<T> creationClubListings,
        Func<T, ModKey> selector);
}

public sealed class OrderListings : IOrderListings
{

    public IEnumerable<T> Order<T>(IEnumerable<T> e, Func<T, ModKey> selector)
    {
        return e.OrderBy(e => selector(e).Type);
    }


    public IEnumerable<T> Order<T>(
        IEnumerable<T> implicitListings,
        IEnumerable<T> pluginsListings,
        IEnumerable<T> creationClubListings,
        Func<T, ModKey> selector)
    {
        var plugins = pluginsListings
            .Select(selector)
            .ToList();
        return implicitListings
            .Concat(
                Order(creationClubListings

                    .OrderBy(selector, Comparer<ModKey>.Create((x, y) =>
                    {
                        var xIndex = plugins.IndexOf(x);
                        var yIndex = plugins.IndexOf(y);
                        if (xIndex == yIndex) return 0;
                        return xIndex - yIndex;
                    })), selector))
            .Concat(pluginsListings)
            .Distinct(selector);
    }
}