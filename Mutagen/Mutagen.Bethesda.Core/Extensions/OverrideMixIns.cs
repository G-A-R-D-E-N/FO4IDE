using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins;
using Noggog;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Exceptions;

namespace Mutagen.Bethesda;

public static class OverrideMixIns
{

















    public static IEnumerable<TMajor> WinningOverrides<TMajor>(
        this IEnumerable<IModListingGetter<IModGetter>> modListings,
        bool includeDeletedRecords = false)
        where TMajor : class, IMajorRecordQueryableGetter
    {
        return modListings
            .Select(l => l.Mod)
            .WhereNotNull()
            .WinningOverrides<TMajor>(includeDeletedRecords: includeDeletedRecords);
    }
















    public static IEnumerable<IMajorRecordGetter> WinningOverrides(
        this IEnumerable<IModListingGetter<IModGetter>> modListings,
        Type type,
        bool includeDeletedRecords = false)
    {
        return modListings
            .Select(l => l.Mod)
            .WhereNotNull()
            .WinningOverrides(type, includeDeletedRecords: includeDeletedRecords);
    }
















    public static IEnumerable<TMajor> WinningOverrides<TMajor>(
        this IEnumerable<IModGetter> mods,
        bool includeDeletedRecords = false)
        where TMajor : class, IMajorRecordQueryableGetter
    {
        var passedRecords = new HashSet<FormKey>();
        foreach (var mod in mods)
        {
            foreach (var record in mod.EnumerateMajorRecords<TMajor>())
            {
                if (record is not IMajorRecordGetter maj) continue;
                if (!passedRecords.Add(maj.FormKey)) continue;
                if (!includeDeletedRecords && maj.IsDeleted) continue;
                yield return record;
            }
        }
    }
















    public static IEnumerable<IMajorRecordGetter> WinningOverrides(
        this IEnumerable<IModGetter> mods,
        Type type,
        bool includeDeletedRecords = false)
    {
        var passedRecords = new HashSet<FormKey>();
        foreach (var mod in mods)
        {
            foreach (var record in mod.EnumerateMajorRecords(type))
            {
                if (!passedRecords.Add(record.FormKey)) continue;
                if (!includeDeletedRecords && record.IsDeleted) continue;
                yield return record;
            }
        }
    }
























    public static IEnumerable<IModContext<TMod, TModGetter, TSetter, TGetter>> WinningContextOverrides<TMod, TModGetter, TSetter, TGetter>(
        this IEnumerable<IModListingGetter<TModGetter>> modListings,
        ILinkCache linkCache,
        bool includeDeletedRecords = false)
        where TMod : class, IMod, TModGetter
        where TModGetter : class, IModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TSetter : class, IMajorRecordQueryable, TGetter
        where TGetter : class, IMajorRecordQueryableGetter
    {
        return modListings
            .Select(l => l.Mod)
            .WhereNotNull()
            .WinningContextOverrides<TMod, TModGetter, TSetter, TGetter>(linkCache, includeDeletedRecords: includeDeletedRecords);
    }






















    public static IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> WinningContextOverrides<TMod, TModGetter>(
        this IEnumerable<IModListingGetter<TModGetter>> modListings,
        ILinkCache linkCache,
        Type type,
        bool includeDeletedRecords = false)
        where TMod : class, IMod, TModGetter
        where TModGetter : class, IModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
    {
        return modListings
            .Select(l => l.Mod)
            .WhereNotNull()
            .WinningContextOverrides<TMod, TModGetter>(linkCache, type, includeDeletedRecords: includeDeletedRecords);
    }






















    public static IEnumerable<IModContext<TMod, TModGetter, TSetter, TGetter>> WinningContextOverrides<TMod, TModGetter, TSetter, TGetter>(
        this IEnumerable<TModGetter> mods,
        ILinkCache linkCache,
        bool includeDeletedRecords = false)
        where TMod : class, IMod, TModGetter
        where TModGetter : class, IModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TSetter : class, IMajorRecordQueryable, TGetter
        where TGetter : class, IMajorRecordQueryableGetter
    {
        var passedRecords = new HashSet<FormKey>();
        foreach (var mod in mods)
        {
            foreach (var record in mod.EnumerateMajorRecordContexts<TSetter, TGetter>(linkCache))
            {
                if (record.Record is not IMajorRecordGetter maj) continue;
                if (!passedRecords.Add(maj.FormKey)) continue;
                if (!includeDeletedRecords && maj.IsDeleted) continue;
                yield return record;
            }
        }
    }






















    public static IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> WinningContextOverrides<TMod, TModGetter>(
        this IEnumerable<TModGetter> mods,
        ILinkCache linkCache,
        Type type,
        bool includeDeletedRecords = false)
        where TMod : class, IMod, TModGetter
        where TModGetter : class, IModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
    {
        var passedRecords = new HashSet<FormKey>();
        foreach (var mod in mods)
        {
            foreach (var record in mod.EnumerateMajorRecordContexts(linkCache, type)
                         .Catch((e) =>
                         {
                             RecordException.EnrichAndThrow(e, mod.ModKey);
                             throw e;
                         }))
            {
                if (!passedRecords.Add(record.Record.FormKey)) continue;
                if (!includeDeletedRecords && record.Record.IsDeleted) continue;
                yield return record;
            }
        }
    }
























    [Obsolete("Use WinningContextOverrides instead")]
    public static IEnumerable<IModContext<TMod, TModGetter, TSetter, TGetter>> WinningOverrideContexts<TMod, TModGetter, TSetter, TGetter>(
        this IEnumerable<IModListingGetter<TModGetter>> modListings,
        ILinkCache linkCache,
        bool includeDeletedRecords = false)
        where TMod : class, IMod, TModGetter
        where TModGetter : class, IModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TSetter : class, IMajorRecordQueryable, TGetter
        where TGetter : class, IMajorRecordQueryableGetter
    {
        return WinningContextOverrides<TMod, TModGetter, TSetter, TGetter>(modListings, linkCache, includeDeletedRecords);
    }






















    [Obsolete("Use WinningContextOverrides instead")]
    public static IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> WinningOverrideContexts<TMod, TModGetter>(
        this IEnumerable<IModListingGetter<TModGetter>> modListings,
        ILinkCache linkCache,
        Type type,
        bool includeDeletedRecords = false)
        where TMod : class, IMod, TModGetter
        where TModGetter : class, IModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
    {
        return WinningContextOverrides<TMod, TModGetter>(modListings, linkCache, type, includeDeletedRecords);
    }






















    [Obsolete("Use WinningContextOverrides instead")]
    public static IEnumerable<IModContext<TMod, TModGetter, TSetter, TGetter>> WinningOverrideContexts<TMod, TModGetter, TSetter, TGetter>(
        this IEnumerable<TModGetter> mods,
        ILinkCache linkCache,
        bool includeDeletedRecords = false)
        where TMod : class, IMod, TModGetter
        where TModGetter : class, IModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TSetter : class, IMajorRecordQueryable, TGetter
        where TGetter : class, IMajorRecordQueryableGetter
    {
        return WinningContextOverrides<TMod, TModGetter, TSetter, TGetter>(mods, linkCache, includeDeletedRecords);
    }






















    [Obsolete("Use WinningContextOverrides instead")]
    public static IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> WinningOverrideContexts<TMod, TModGetter>(
        this IEnumerable<TModGetter> mods,
        ILinkCache linkCache,
        Type type,
        bool includeDeletedRecords = false)
        where TMod : class, IMod, TModGetter
        where TModGetter : class, IModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
    {
        return WinningContextOverrides<TMod, TModGetter>(mods, linkCache, type, includeDeletedRecords);
    }
}