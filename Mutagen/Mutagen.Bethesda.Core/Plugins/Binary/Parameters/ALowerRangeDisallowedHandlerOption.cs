using Mutagen.Bethesda.Plugins.Order;

namespace Mutagen.Bethesda.Plugins.Binary.Parameters;









public abstract class ALowerRangeDisallowedHandlerOption
{
    public static NoCheckIfLowerRangeDisallowed NoCheck { get; } = new NoCheckIfLowerRangeDisallowed();
    public static ThrowIfLowerRangeDisallowed Throw { get; } = new ThrowIfLowerRangeDisallowed();

    public static AddPlaceholderMasterIfLowerRangeDisallowed AddPlaceholder(ModKey key)
    {
        return new AddPlaceholderMasterIfLowerRangeDisallowed()
        {
            ModKey = key
        };
    }

    public static AddPlaceholderMasterIfLowerRangeDisallowed AddPlaceholder(ILoadOrderGetter loadOrder)
    {
        return new AddPlaceholderMasterIfLowerRangeDisallowed()
        {
            ModKey = loadOrder.ListedOrder.Select<ModKey, ModKey?>(x => x).FirstOrDefault()
        };
    }

    public static AddPlaceholderMasterIfLowerRangeDisallowed AddPlaceholder(IEnumerable<ModKey> loadOrder)
    {
        return new AddPlaceholderMasterIfLowerRangeDisallowed()
        {
            ModKey = loadOrder.Select<ModKey, ModKey?>(x => x).FirstOrDefault()
        };
    }

    internal static ALowerRangeDisallowedHandlerOption AddPlaceholderIfNotSkipping(
        ModKey key,
        ALowerRangeDisallowedHandlerOption existing)
    {
        return SetIfNotSkipping(AddPlaceholder(key), existing);
    }

    internal static ALowerRangeDisallowedHandlerOption AddPlaceholderIfNotSkipping(
        ILoadOrderGetter loadOrder,
        ALowerRangeDisallowedHandlerOption existing)
    {
        return SetIfNotSkipping(AddPlaceholder(loadOrder), existing);
    }

    internal static ALowerRangeDisallowedHandlerOption AddPlaceholderIfNotSkipping(
        IEnumerable<ModKey> loadOrder,
        ALowerRangeDisallowedHandlerOption existing)
    {
        return SetIfNotSkipping(AddPlaceholder(loadOrder), existing);
    }

    internal static ALowerRangeDisallowedHandlerOption SetIfNotSkipping(
        ALowerRangeDisallowedHandlerOption option,
        ALowerRangeDisallowedHandlerOption existing)
    {
        if (existing is NoCheckIfLowerRangeDisallowed) return existing;
        return option;
    }
}






public class NoCheckIfLowerRangeDisallowed : ALowerRangeDisallowedHandlerOption
{
}







public class ThrowIfLowerRangeDisallowed : ALowerRangeDisallowedHandlerOption
{
}







public class AddPlaceholderMasterIfLowerRangeDisallowed : ALowerRangeDisallowedHandlerOption
{
    public ModKey? ModKey { get; init; }
}
