using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Cache.Internals;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda;

public static class GetOrAddAsOverrideMixIns
{

    public static TMajor GetOrAddAsOverride<TMajor, TMajorGetter>(this IGroup<TMajor> group, TMajorGetter major)
        where TMajor : IMajorRecordInternal, TMajorGetter
        where TMajorGetter : IMajorRecordGetter
    {
        try
        {
            if (group.RecordCache.TryGetValue(major.FormKey, out var existingMajor))
            {
                return existingMajor;
            }
            var mask = OverrideMaskRegistrations.Get<TMajor>();
            var copy = major.DeepCopy(mask as MajorRecord.TranslationMask);
            if (copy is not TMajor rhs)
            {
                throw new InvalidOperationException($"DeepCopy did not return a record of the expected type {typeof(TMajor).Name}");
            }
            existingMajor = rhs;
            group.RecordCache.Set(existingMajor);
            return existingMajor;
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow<TMajor>(ex, major.FormKey, major.EditorID);
            throw;
        }
    }

    public static bool TryGetOrAddAsOverride<TMajor, TMajorGetter>(this IGroup<TMajor> group, IFormLinkGetter<TMajorGetter> link, ILinkCache cache, [MaybeNullWhen(false)] out TMajor rec)
        where TMajor : class, IMajorRecordInternal, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter
    {
        try
        {
            if (group.RecordCache.TryGetValue(link.FormKey, out rec))
            {
                return true;
            }
            if (!link.TryResolve<TMajorGetter>(cache, out var getter))
            {
                rec = default;
                return false;
            }
            rec = GetOrAddAsOverride(group, getter);
            return true;
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow<TMajor>(ex, link.FormKey, edid: null);
            throw;
        }
    }

    public static TMajor GetOrAddAsOverride<TMajor, TMajorGetter>(this IGroup<TMajor> group, IFormLinkGetter<TMajorGetter> link, ILinkCache cache)
        where TMajor : class, IMajorRecordInternal, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter
    {
        if (TryGetOrAddAsOverride(group, link, cache, out var rec))
        {
            return rec;
        }
        throw new MissingRecordException(link.FormKey, link.Type);
    }

    public static bool TryGetOrAddAsOverrideUntyped(
        this IGroup group,
        IMajorRecordGetter major,
        [MaybeNullWhen(false)] out IMajorRecord result)
    {
        try
        {

            if (!group.ContainedRecordType.IsAssignableFrom(major.GetType()))
            {
                result = null;
                return false;
            }

            if (group.RecordCache.TryGetValue(major.FormKey, out var existingMajor))
            {
                if (existingMajor is IMajorRecord existingRecord)
                {
                    result = existingRecord;
                    return true;
                }
                result = null;
                return false;
            }

            var mask = OverrideMaskRegistrations.Get(major.GetType());

            var copy = major.DeepCopy(mask as MajorRecord.TranslationMask);
            if (copy is not IMajorRecord rhs)
            {
                result = null;
                return false;
            }

            group.SetUntyped(rhs);
            result = rhs;
            return true;
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow(ex, major.FormKey, major.GetType(), major.EditorID);
            throw;
        }
    }
}