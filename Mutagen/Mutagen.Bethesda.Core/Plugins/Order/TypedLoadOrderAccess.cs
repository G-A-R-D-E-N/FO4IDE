using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Order;

public readonly struct TypedLoadOrderAccess<TMod, TModGetter, TMajor, TMajorGetter>
    where TModGetter : IModGetter
    where TMod : IMod, TModGetter
    where TMajor : class, IMajorRecord, TMajorGetter
    where TMajorGetter : class, IMajorRecordGetter
{
    private readonly Func<bool, IEnumerable<TMajorGetter>> _winningOverrides;
    private readonly Func<ILinkCache, bool, IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>>> _winningContextOverrides;

    public TypedLoadOrderAccess(
        Func<bool, IEnumerable<TMajorGetter>> winningOverrides,
        Func<ILinkCache, bool, IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>>> winningContextOverrides)
    {
        _winningOverrides = winningOverrides;
        _winningContextOverrides = winningContextOverrides;
    }

    public IEnumerable<TMajorGetter> WinningOverrides(bool includeDeletedRecords = false) => _winningOverrides(includeDeletedRecords);

    public IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>> WinningContextOverrides(ILinkCache linkCache, bool includeDeletedRecords = false) => _winningContextOverrides(linkCache, includeDeletedRecords);
}

public readonly struct TopLevelTypedLoadOrderAccess<TMod, TModGetter, TMajor, TMajorGetter>
    where TModGetter : IModGetter
    where TMod : IMod, TModGetter
    where TMajor : class, IMajorRecord, TMajorGetter
    where TMajorGetter : class, IMajorRecordGetter
{
    private readonly Func<bool, IEnumerable<TMajorGetter>> _winningOverrides;
    private readonly Func<ILinkCache, bool, IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>>> _winningContextOverrides;

    public TopLevelTypedLoadOrderAccess(
        Func<bool, IEnumerable<TMajorGetter>> winningOverrides,
        Func<ILinkCache, bool, IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>>> winningContextOverrides)
    {
        _winningOverrides = winningOverrides;
        _winningContextOverrides = winningContextOverrides;
    }

    public IEnumerable<TMajorGetter> WinningOverrides(bool includeDeletedRecords = false) => _winningOverrides(includeDeletedRecords);

    public IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>> WinningContextOverrides(bool includeDeletedRecords = false)
        => _winningContextOverrides(

            default(ILinkCache?)!,
            includeDeletedRecords);
}