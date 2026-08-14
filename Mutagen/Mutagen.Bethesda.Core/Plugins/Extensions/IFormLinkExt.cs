using System.Diagnostics.CodeAnalysis;
using Loqui;
using Loqui.Interfaces;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Mapping;

namespace Mutagen.Bethesda;

public static class IFormLinkExt
{
    #region Resolve

    public static bool TryResolve<TMajor>(this IFormLinkGetter<TMajor> link, ILinkCache cache, [MaybeNullWhen(false)] out TMajor majorRecord)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            majorRecord = default;
            return false;
        }
        return cache.TryResolve<TMajor>(formKey, out majorRecord);
    }

    public static bool TryResolve<TSource, TScopedMajor>(this IFormLinkGetter<TSource> link, ILinkCache cache, [MaybeNullWhen(false)] out TScopedMajor majorRecord)
        where TSource : class, IMajorRecordGetter
        where TScopedMajor : class, TSource
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            majorRecord = default;
            return false;
        }
        return cache.TryResolve(formKey, out majorRecord);
    }

    public static bool TryResolve<TMajor>(this IFormLinkGetter link, ILinkCache cache, [MaybeNullWhen(false)] out TMajor majorRecord)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            majorRecord = default;
            return false;
        }
        return cache.TryResolve(formKey, out majorRecord);
    }

    public static TMajor? TryResolve<TMajor>(this IFormLinkGetter<TMajor> link, ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.FormKeyNullable == null)
        {
            return null;
        }
        if (link.TryResolve<TMajor>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        return null;
    }

    public static TScopedMajor? TryResolve<TMajor, TScopedMajor>(this IFormLinkGetter<TMajor> link, ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
        where TScopedMajor : class, TMajor
    {
        if (link.TryResolve<TMajor, TScopedMajor>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        return null;
    }

    public static TMajor? TryResolve<TMajor>(this IFormLinkGetter link, ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.TryResolve<TMajor>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        return null;
    }

    public static TScopedMajor Resolve<TMajor, TScopedMajor>(this IFormLinkGetter<TMajor> link, ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
        where TScopedMajor : class, TMajor
    {
        if (link.TryResolve<TMajor, TScopedMajor>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        throw RecordException.Create<TScopedMajor>(
            formKey: link.FormKeyNullable,
            modKey: link.FormKeyNullable?.ModKey,
            edid: null,
            message: "Could not resolve record");
    }

    public static TMajor Resolve<TMajor>(this IFormLinkGetter<TMajor> link, ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.FormKeyNullable == null)
        {
            throw new RecordException(null, null, null, "Could not resolve record");
        }
        if (link.TryResolve<TMajor>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        throw RecordException.Create<TMajor>(
            formKey: link.FormKeyNullable,
            modKey: link.FormKeyNullable?.ModKey,
            edid: null,
            message: "Could not resolve record");
    }

    public static TMajor Resolve<TMajor>(this IFormLinkGetter link, ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.TryResolve<TMajor>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        throw RecordException.Create<TMajor>(
            message: "Could not resolve record",
            formKey: link.FormKeyNullable,
            modKey: link.FormKeyNullable?.ModKey,
            edid: null);
    }
    #endregion

    #region ResolveAll

    public static IEnumerable<TMajor> ResolveAll<TMajor>(this IFormLinkGetter<TMajor> link, ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            return [];
        }
        return cache.ResolveAll<TMajor>(formKey);
    }

    public static IEnumerable<TScopedMajor> ResolveAll<TSource, TScopedMajor>(this IFormLinkGetter<TSource> link, ILinkCache cache)
        where TSource : class, IMajorRecordGetter
        where TScopedMajor : class, TSource
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            return [];
        }
        return cache.ResolveAll<TScopedMajor>(formKey);
    }
    #endregion

    #region Resolve Context

    public static bool TryResolveContext<TMod, TModGetter, TMajor, TMajorGetter>(
        this IFormLinkGetter<TMajorGetter> link,
        ILinkCache<TMod, TModGetter> cache,
        [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, TMajor, TMajorGetter> majorRecord)
        where TModGetter : class, IModGetter
        where TMod : class, TModGetter, IContextMod<TMod, TModGetter>
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            majorRecord = default;
            return false;
        }
        return cache.TryResolveContext<TMajor, TMajorGetter>(formKey, out majorRecord);
    }

    public static IModContext<TMod, TModGetter, TMajor, TMajorGetter>? ResolveContext<TMod, TModGetter, TMajor, TMajorGetter>(
        this IFormLinkGetter<TMajorGetter> link,
        ILinkCache<TMod, TModGetter> cache)
        where TModGetter : class, IModGetter
        where TMod : class, TModGetter, IContextMod<TMod, TModGetter>
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter
    {
        if (link.TryResolveContext<TMod, TModGetter, TMajor, TMajorGetter>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        return null;
    }

    public static bool TryResolveContext<TMod, TModGetter, TMajorGetter, TScopedSetter, TScopedGetter>(
        this IFormLinkGetter<TMajorGetter> link,
        ILinkCache<TMod, TModGetter> cache,
        [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, TScopedSetter, TScopedGetter> majorRecord)
        where TModGetter : class, IModGetter
        where TMod : class, TModGetter, IContextMod<TMod, TModGetter>
        where TMajorGetter : class, IMajorRecordGetter
        where TScopedSetter : class, TScopedGetter, IMajorRecord
        where TScopedGetter : class, TMajorGetter
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            majorRecord = default;
            return false;
        }
        return cache.TryResolveContext<TScopedSetter, TScopedGetter>(formKey, out majorRecord);
    }

    public static IModContext<TMod, TModGetter, TScopedSetter, TScopedGetter>? ResolveContext<TMod, TModGetter, TMajorGetter, TScopedSetter, TScopedGetter>(
        this IFormLinkGetter<TMajorGetter> link,
        ILinkCache<TMod, TModGetter> cache)
        where TModGetter : class, IModGetter
        where TMod : class, TModGetter, IContextMod<TMod, TModGetter>
        where TMajorGetter : class, IMajorRecordGetter
        where TScopedSetter : class, TScopedGetter, IMajorRecord
        where TScopedGetter : class, TMajorGetter
    {
        if (link.TryResolveContext<TMod, TModGetter, TMajorGetter, TScopedSetter, TScopedGetter>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        return null;
    }
    #endregion

    #region ResolveAll Context

    public static IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>> ResolveAllContexts<TMod, TModGetter, TMajor, TMajorGetter>(
        this IFormLinkGetter<TMajorGetter> link,
        ILinkCache<TMod, TModGetter> cache)
        where TModGetter : class, IModGetter
        where TMod : class, TModGetter, IContextMod<TMod, TModGetter>
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            return [];
        }
        return cache.ResolveAllContexts<TMajor, TMajorGetter>(formKey);
    }

    public static IEnumerable<IModContext<TMod, TModGetter, TScopedSetter, TScopedGetter>> ResolveAllContexts<TMod, TModGetter, TMajorGetter, TScopedSetter, TScopedGetter>(
        this IFormLinkGetter<TMajorGetter> link,
        ILinkCache<TMod, TModGetter> cache)
        where TModGetter : class, IModGetter
        where TMod : class, TModGetter, IContextMod<TMod, TModGetter>
        where TMajorGetter : class, IMajorRecordGetter
        where TScopedSetter : class, TScopedGetter, IMajorRecord
        where TScopedGetter : class, TMajorGetter
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            return [];
        }
        return cache.ResolveAllContexts<TScopedSetter, TScopedGetter>(formKey);
    }
    #endregion

    #region Resolve Simple Context

    public static bool TryResolveSimpleContext<TMajor>(
        this IFormLinkGetter<TMajor> link,
        ILinkCache cache,
        [MaybeNullWhen(false)] out IModContext<TMajor> majorRecord)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            majorRecord = default;
            return false;
        }
        return cache.TryResolveSimpleContext<TMajor>(formKey, out majorRecord);
    }

    public static IModContext<TMajor>? ResolveSimpleContext<TMajor>(
        this IFormLinkGetter<TMajor> link,
        ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.TryResolveSimpleContext<TMajor>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        return null;
    }

    public static bool TryResolveSimpleContext<TMajor, TScoped>(
        this IFormLinkGetter<TMajor> link,
        ILinkCache cache,
        [MaybeNullWhen(false)] out IModContext<TScoped> majorRecord)
        where TMajor : class, IMajorRecordGetter
        where TScoped : class, TMajor
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            majorRecord = default;
            return false;
        }
        return cache.TryResolveSimpleContext<TScoped>(formKey, out majorRecord);
    }

    public static IModContext<TScoped>? ResolveSimpleContext<TMajor, TScoped>(
        this IFormLinkGetter<TMajor> link,
        ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
        where TScoped : class, TMajor
    {
        if (link.TryResolveSimpleContext<TMajor, TScoped>(cache, out var majorRecord))
        {
            return majorRecord;
        }
        return null;
    }
    #endregion

    #region ResolveAll Simple Context

    public static IEnumerable<IModContext<TMajor>> ResolveAllSimpleContexts<TMajor>(
        this IFormLinkGetter<TMajor> link,
        ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            return [];
        }
        return cache.ResolveAllSimpleContexts<TMajor>(formKey);
    }

    public static IEnumerable<IModContext<TScoped>> ResolveAllSimpleContexts<TMajor, TScoped>(
        this IFormLinkGetter<TMajor> link,
        ILinkCache cache)
        where TMajor : class, IMajorRecordGetter
        where TScoped : class, TMajor
    {
        if (link.FormKeyNullable is not {} formKey)
        {
            return [];
        }
        return cache.ResolveAllSimpleContexts<TScoped>(formKey);
    }
    #endregion

    #region ResolveIdentifier

    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    public static bool TryResolveIdentifier(
        this IFormLinkGetter formLink,
        ILinkCache cache, [MaybeNullWhen(false)] out string? editorId,
        ResolveTarget target = ResolveTarget.Winner)
    {
        return cache.TryResolveIdentifier(formLink.FormKey, out editorId);
    }

    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    public static string? ResolveIdentifier(
        this IFormLinkGetter formLink,
        ILinkCache cache,
        ResolveTarget target = ResolveTarget.Winner)
    {
        return cache.ResolveIdentifier(formLink.FormKey);
    }

    public static bool TryResolveIdentifier(
        this IFormLinkGetter formLink,
        Type type,
        ILinkCache cache, [MaybeNullWhen(false)] out string? editorId,
        ResolveTarget target = ResolveTarget.Winner)
    {
        return cache.TryResolveIdentifier(formLink.FormKey, type, out editorId);
    }

    public static string? ResolveIdentifier(
        this IFormLinkGetter formLink,
        Type type,
        ILinkCache cache,
        ResolveTarget target = ResolveTarget.Winner)
    {
        return cache.ResolveIdentifier(formLink.FormKey, type);
    }

    public static bool TryResolveIdentifier<TMajor>(
        this IFormLinkGetter<TMajor> formLink,
        ILinkCache cache, [MaybeNullWhen(false)] out string? editorId,
        ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter
    {
        return cache.TryResolveIdentifier<TMajor>(formLink.FormKey, out editorId);
    }

    public static string? ResolveIdentifier<TMajor>(
        this IFormLinkGetter<TMajor> formLink,
        ILinkCache cache,
        ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter
    {
        return cache.ResolveIdentifier<TMajor>(formLink.FormKey);
    }

    #endregion

    internal static bool EqualsWithInheritanceConsideration<TMajorGetter>(IFormLinkGetter<TMajorGetter> link, object? obj)
        where TMajorGetter : class, IMajorRecordGetter
    {
        if (obj == null)
        {
            return link.IsNull;
        }
        else if (obj is IFormLinkGetter<TMajorGetter> rhs)
        {
            return link.FormKey == rhs.FormKey;
        }
        else if (obj is IFormLinkGetter rhsLink
                 && rhsLink.Type.IsAssignableFrom(typeof(TMajorGetter)))
        {
            return link.FormKey == rhsLink.FormKey;
        }
        else if (obj is TMajorGetter maj)
        {
            return link.FormKey == maj.FormKey;
        }
        else
        {
            return false;
        }
    }

    internal class FormLinkInformationEqualityComparerWithDualInheritanceConsiderationComparer : IEqualityComparer<IFormLinkIdentifier>
    {
        public bool Equals(IFormLinkIdentifier? x, IFormLinkIdentifier? y)
        {
            if (x is null && y is null)
            {
                return true;
            }
            if (x is null || y is null)
            {
                return false;
            }
            if (y.Type.IsAssignableFrom(x.Type)
                || x.Type.IsAssignableFrom(y.Type))
            {
                return x.FormKey == y.FormKey;
            }

            return false;
        }

        public int GetHashCode(IFormLinkIdentifier obj)
        {
            return obj.FormKey.GetHashCode();
        }
    }

    internal static IEqualityComparer<IFormLinkIdentifier> FormLinkInformationEqualityComparerWithDualInheritanceConsideration { get; } = new FormLinkInformationEqualityComparerWithDualInheritanceConsiderationComparer();

    public static void SetTo<TMajorLhs, TMajorRhs>(this IFormLink<TMajorLhs> link, TMajorRhs? record)
        where TMajorLhs : class, IMajorRecordGetter
        where TMajorRhs : class, TMajorLhs
    {
        link.SetTo(record?.FormKey);
    }

    public static void SetTo<TMajor, TMajorGetter>(this IFormLink<TMajor> link, IFormLinkGetter<TMajorGetter> rhs)
        where TMajor : class, IMajorRecordGetter
        where TMajorGetter : class, TMajor
    {
        link.SetTo(rhs.FormKeyNullable);
    }

    public static void SetToGetter<TMajor, TMajorGetter>(this IFormLink<TMajor> link, IFormLinkGetter<TMajorGetter> rhs)
        where TMajor : class, IMapsToGetter<TMajorGetter>, IMajorRecord
        where TMajorGetter : class, IMajorRecordGetter
    {
        link.SetTo(rhs.FormKeyNullable);
    }

    public static IFormLinkNullable<TMajor> AsNullable<TMajor>(this IFormLinkGetter<TMajor> link)
        where TMajor : class, IMajorRecordGetter
    {
        return new FormLinkNullable<TMajor>(link.FormKeyNullable);
    }

    public static IFormLink<TMajor> AsSetter<TMajor>(this IFormLinkGetter<TMajor> link)
        where TMajor : class, IMajorRecordGetter
    {
        return new FormLink<TMajor>(link.FormKey);
    }

    #region Standardize

    public static IFormLinkIdentifier ToStandardizedIdentifier(this IFormLinkIdentifier identifier)
    {
        if (!identifier.TryToStandardizedIdentifier(out var standardized))
        {
            throw new ArgumentException($"Could not standardize type: {identifier}");
        }

        return standardized;
    }

    public static bool TryToStandardizedIdentifier(this IFormLinkIdentifier identifier, [MaybeNullWhen(false)] out IFormLinkIdentifier standardized)
    {
        if (GetterTypeMapping.Instance.TryGetGetterType(identifier.Type, out var getterType))
        {
            if (identifier.Type == getterType)
            {
                standardized = identifier;
                return true;
            }
            else
            {
                standardized = new FormLinkInformation(identifier.FormKey, getterType);
                return true;
            }
        }

        standardized = default;
        return false;
    }
    #endregion
}