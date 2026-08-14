using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Cache;

public interface IIdentifierLinkCache : IDisposable
{

    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    bool TryResolveIdentifier(FormKey formKey, [MaybeNullWhen(false)] out string? editorId, ResolveTarget target = ResolveTarget.Winner);

    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    string? ResolveIdentifier(FormKey formKey, ResolveTarget target = ResolveTarget.Winner);

    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    bool TryResolveIdentifier(string editorId, [MaybeNullWhen(false)] out FormKey formKey);

    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    FormKey ResolveIdentifier(string editorId);

    bool TryResolveIdentifier(FormKey formKey, Type type, [MaybeNullWhen(false)] out string? editorId, ResolveTarget target = ResolveTarget.Winner);

    string? ResolveIdentifier(FormKey formKey, Type type, ResolveTarget target = ResolveTarget.Winner);

    bool TryResolveIdentifier(IFormLinkIdentifier formLink, out string? editorId, ResolveTarget target = ResolveTarget.Winner);

    string? ResolveIdentifier(IFormLinkIdentifier formLink, ResolveTarget target = ResolveTarget.Winner);

    bool TryResolveIdentifier(string editorId, Type type, [MaybeNullWhen(false)] out FormKey formKey);

    FormKey ResolveIdentifier(string editorId, Type type);

    bool TryResolveIdentifier<TMajor>(FormKey formKey, [MaybeNullWhen(false)] out string? editorId, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryableGetter;

    string? ResolveIdentifier<TMajor>(FormKey formKey, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryableGetter;

    bool TryResolveIdentifier<TMajor>(string editorId, [MaybeNullWhen(false)] out FormKey formKey)
        where TMajor : class, IMajorRecordQueryableGetter;

    FormKey ResolveIdentifier<TMajor>(string editorId)
        where TMajor : class, IMajorRecordQueryableGetter;

    bool TryResolveIdentifier(FormKey formKey, [MaybeNullWhen(false)] out string? editorId, params Type[] types);

    string? ResolveIdentifier(FormKey formKey, params Type[] types);

    bool TryResolveIdentifier(string editorId, [MaybeNullWhen(false)] out FormKey formKey, params Type[] types);

    FormKey ResolveIdentifier(string editorId, params Type[] types);

    bool TryResolveIdentifier(FormKey formKey, IEnumerable<Type> types, [MaybeNullWhen(false)] out string? editorId, ResolveTarget target = ResolveTarget.Winner);

    bool TryResolveIdentifier(FormKey formKey, IEnumerable<Type> types, [MaybeNullWhen(false)] out string? editorId, [MaybeNullWhen(false)] out Type matchedType, ResolveTarget target = ResolveTarget.Winner);

    string? ResolveIdentifier(FormKey formKey, IEnumerable<Type> types, ResolveTarget target = ResolveTarget.Winner);

    bool TryResolveIdentifier(string editorId, IEnumerable<Type> types, [MaybeNullWhen(false)] out FormKey formKey);

    bool TryResolveIdentifier(string editorId, IEnumerable<Type> types, [MaybeNullWhen(false)] out FormKey formKey, [MaybeNullWhen(false)] out Type matchedType);

    FormKey ResolveIdentifier(string editorId, IEnumerable<Type> types);

    IEnumerable<IMajorRecordIdentifierGetter> AllIdentifiers(Type type, CancellationToken? cancel = null);

    IEnumerable<IMajorRecordIdentifierGetter> AllIdentifiers<TMajor>(CancellationToken? cancel = null)
        where TMajor : class, IMajorRecordQueryableGetter;

    IEnumerable<IMajorRecordIdentifierGetter> AllIdentifiers(IEnumerable<Type> types, CancellationToken? cancel = null);

    IEnumerable<IMajorRecordIdentifierGetter> AllIdentifiers(params Type[] types);
}