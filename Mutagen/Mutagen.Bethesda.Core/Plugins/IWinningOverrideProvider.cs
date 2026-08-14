using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins;

public interface IWinningOverrideProvider
{














    IEnumerable<TMajor> WinningOverrides<TMajor>(bool includeDeletedRecords = false)
        where TMajor : class, IMajorRecordQueryableGetter;















    IEnumerable<IMajorRecordGetter> WinningOverrides(Type type, bool includeDeletedRecords = false);
}

public interface IWinningOverrideProvider<TMod, TModGetter> : IWinningOverrideProvider
    where TModGetter : class, IModGetter
    where TMod : class, TModGetter, IMod
{




















    IEnumerable<IModContext<TMod, TModGetter, TSetter, TGetter>> WinningContextOverrides<TSetter, TGetter>(
        ILinkCache linkCache,
        bool includeDeletedRecords = false)
        where TSetter : class, IMajorRecordQueryable, TGetter
        where TGetter : class, IMajorRecordQueryableGetter;





















    IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> WinningContextOverrides(
        ILinkCache linkCache,
        Type type,
        bool includeDeletedRecords = false);
}