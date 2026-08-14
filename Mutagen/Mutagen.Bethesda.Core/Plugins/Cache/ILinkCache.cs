using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Cache;




public interface ILinkCache : IIdentifierLinkCache, IWinningOverrideProvider
{










    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    bool TryResolve(FormKey formKey, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec, ResolveTarget target = ResolveTarget.Winner);










    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    bool TryResolve(string editorId, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec);


















    bool TryResolve<TMajor>(FormKey formKey, [MaybeNullWhen(false)] out TMajor majorRec, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryableGetter;


















    bool TryResolve<TMajor>(IFormLinkGetter<TMajor> formLink, [MaybeNullWhen(false)] out TMajor majorRec, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter;


















    bool TryResolve<TMajor>(TMajor record, [MaybeNullWhen(false)] out TMajor majorRec, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter;

















    bool TryResolve<TMajor>(string editorId, [MaybeNullWhen(false)] out TMajor majorRec)
        where TMajor : class, IMajorRecordQueryableGetter;


















    bool TryResolve(FormKey formKey, Type type, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec, ResolveTarget target = ResolveTarget.Winner);


















    bool TryResolve(IFormLinkIdentifier formLink, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec, ResolveTarget target = ResolveTarget.Winner);

















    bool TryResolve(string editorId, Type type, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec);




















    bool TryResolve(FormKey formKey, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec, params Type[] types);




















    bool TryResolve(string editorId, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec, params Type[] types);





















    bool TryResolve(FormKey formKey, IEnumerable<Type> types, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec, ResolveTarget target = ResolveTarget.Winner);






















    bool TryResolve(FormKey formKey, IEnumerable<Type> types, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec, [MaybeNullWhen(false)] out Type matchedType, ResolveTarget target = ResolveTarget.Winner);




















    bool TryResolve(string editorId, IEnumerable<Type> types, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec);





















    bool TryResolve(string editorId, IEnumerable<Type> types, [MaybeNullWhen(false)] out IMajorRecordGetter majorRec, [MaybeNullWhen(false)] out Type matchedType);
















    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    IMajorRecordGetter Resolve(FormKey formKey, ResolveTarget target = ResolveTarget.Winner);















    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    IMajorRecordGetter Resolve(string editorId);



















    IMajorRecordGetter Resolve(FormKey formKey, Type type, ResolveTarget target = ResolveTarget.Winner);


















    IMajorRecordGetter Resolve(IFormLinkIdentifier formLink, ResolveTarget target = ResolveTarget.Winner);


















    IMajorRecordGetter Resolve(string editorId, Type type);





















    IMajorRecordGetter Resolve(FormKey formKey, params Type[] types);





















    IMajorRecordGetter Resolve(string editorId, params Type[] types);






















    IMajorRecordGetter Resolve(FormKey formKey, IEnumerable<Type> types, ResolveTarget target = ResolveTarget.Winner);





















    IMajorRecordGetter Resolve(string editorId, IEnumerable<Type> types);



















    TMajor Resolve<TMajor>(FormKey formKey, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryableGetter;



















    TMajor Resolve<TMajor>(IFormLinkGetter<TMajor> formLink, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter;



















    TMajor Resolve<TMajor>(TMajor record, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter;


















    TMajor Resolve<TMajor>(string editorId)
        where TMajor : class, IMajorRecordQueryableGetter;
















    IEnumerable<TMajor> ResolveAll<TMajor>(FormKey formKey, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryableGetter;
















    IEnumerable<TMajor> ResolveAll<TMajor>(TMajor record, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter;
















    IEnumerable<IMajorRecordGetter> ResolveAll(FormKey formKey, Type type, ResolveTarget target = ResolveTarget.Winner);















    IEnumerable<IMajorRecordGetter> ResolveAll(IFormLinkIdentifier formLink, ResolveTarget target = ResolveTarget.Winner);












    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    IEnumerable<IMajorRecordGetter> ResolveAll(FormKey formKey, ResolveTarget target = ResolveTarget.Winner);











    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    bool TryResolveSimpleContext(FormKey formKey, [MaybeNullWhen(false)] out IModContext<IMajorRecordGetter> majorRec, ResolveTarget target = ResolveTarget.Winner);










    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    bool TryResolveSimpleContext(string editorId, [MaybeNullWhen(false)] out IModContext<IMajorRecordGetter> majorRec);

















    bool TryResolveSimpleContext<TMajor>(FormKey formKey, [MaybeNullWhen(false)] out IModContext<TMajor> majorRec, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryableGetter;

















    bool TryResolveSimpleContext<TMajor>(IFormLinkGetter<TMajor> formLink, [MaybeNullWhen(false)] out IModContext<TMajor> majorRec, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter;
















    bool TryResolveSimpleContext<TMajor>(string editorId, [MaybeNullWhen(false)] out IModContext<TMajor> majorRec)
        where TMajor : class, IMajorRecordQueryableGetter;


















    bool TryResolveSimpleContext(FormKey formKey, Type type, [MaybeNullWhen(false)] out IModContext<IMajorRecordGetter> majorRec, ResolveTarget target = ResolveTarget.Winner);

















    bool TryResolveSimpleContext(IFormLinkIdentifier formLink, [MaybeNullWhen(false)] out IModContext<IMajorRecordGetter> majorRec, ResolveTarget target = ResolveTarget.Winner);

















    bool TryResolveSimpleContext(string editorId, Type type, [MaybeNullWhen(false)] out IModContext<IMajorRecordGetter> majorRec);
















    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    IModContext<IMajorRecordGetter> ResolveSimpleContext(FormKey formKey, ResolveTarget target = ResolveTarget.Winner);















    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    IModContext<IMajorRecordGetter> ResolveSimpleContext(string editorId);



















    IModContext<IMajorRecordGetter> ResolveSimpleContext(FormKey formKey, Type type, ResolveTarget target = ResolveTarget.Winner);


















    IModContext<IMajorRecordGetter> ResolveSimpleContext(IFormLinkIdentifier formLink, ResolveTarget target = ResolveTarget.Winner);


















    IModContext<TMajor> ResolveSimpleContext<TMajor>(TMajor record, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter;


















    IModContext<IMajorRecordGetter> ResolveSimpleContext(string editorId, Type type);



















    IModContext<TMajor> ResolveSimpleContext<TMajor>(FormKey formKey, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryableGetter;



















    IModContext<TMajor> ResolveSimpleContext<TMajor>(IFormLinkGetter<TMajor> formLink, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter;


















    IModContext<TMajor> ResolveSimpleContext<TMajor>(string editorId)
        where TMajor : class, IMajorRecordQueryableGetter;
















    IEnumerable<IModContext<TMajor>> ResolveAllSimpleContexts<TMajor>(FormKey formKey, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryableGetter;
















    IEnumerable<IModContext<IMajorRecordGetter>> ResolveAllSimpleContexts(FormKey formKey, Type type, ResolveTarget target = ResolveTarget.Winner);















    IEnumerable<IModContext<IMajorRecordGetter>> ResolveAllSimpleContexts(IFormLinkIdentifier formLink, ResolveTarget target = ResolveTarget.Winner);















    IEnumerable<IModContext<TMajor>> ResolveAllSimpleContexts<TMajor>(TMajor record, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordGetter;












    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    IEnumerable<IModContext<IMajorRecordGetter>> ResolveAllSimpleContexts(FormKey formKey, ResolveTarget target = ResolveTarget.Winner);





    void Warmup(Type type);





    void Warmup<TMajor>();





    void Warmup(params Type[] types);





    void Warmup(IEnumerable<Type> types);




    IReadOnlyList<IModGetter> ListedOrder { get; }




    IReadOnlyList<IModGetter> PriorityOrder { get; }
}

public interface ILinkCache<TMod, TModGetter> : ILinkCache, IWinningOverrideProvider<TMod, TModGetter>
    where TModGetter : class, IModGetter
    where TMod : class, TModGetter, IMod
{










    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    bool TryResolveContext(FormKey formKey, [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> majorRec, ResolveTarget target = ResolveTarget.Winner);










    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    bool TryResolveContext(string editorId, [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> majorRec);

















    bool TryResolveContext<TMajor, TMajorGetter>(FormKey formKey, [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, TMajor, TMajorGetter> majorRec, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryable, TMajorGetter
        where TMajorGetter : class, IMajorRecordQueryableGetter;

















    bool TryResolveContext<TMajor, TMajorGetter>(IFormLinkGetter<TMajorGetter> formLink, [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, TMajor, TMajorGetter> majorRec, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter;
















    bool TryResolveContext<TMajor, TMajorGetter>(string editorId, [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, TMajor, TMajorGetter> majorRec)
        where TMajor : class, IMajorRecordQueryable, TMajorGetter
        where TMajorGetter : class, IMajorRecordQueryableGetter;


















    bool TryResolveContext(FormKey formKey, Type type, [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> majorRec, ResolveTarget target = ResolveTarget.Winner);

















    bool TryResolveContext(IFormLinkIdentifier formLink, [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> majorRec, ResolveTarget target = ResolveTarget.Winner);

















    bool TryResolveContext(string editorId, Type type, [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> majorRec);
















    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> ResolveContext(FormKey formKey, ResolveTarget target = ResolveTarget.Winner);















    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> ResolveContext(string editorId);



















    IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> ResolveContext(FormKey formKey, Type type, ResolveTarget target = ResolveTarget.Winner);


















    IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> ResolveContext(IFormLinkIdentifier formLink, ResolveTarget target = ResolveTarget.Winner);


















    IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter> ResolveContext(string editorId, Type type);




















    IModContext<TMod, TModGetter, TMajor, TMajorGetter> ResolveContext<TMajor, TMajorGetter>(FormKey formKey, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryable, TMajorGetter
        where TMajorGetter : class, IMajorRecordQueryableGetter;




















    IModContext<TMod, TModGetter, TMajor, TMajorGetter> ResolveContext<TMajor, TMajorGetter>(IFormLinkGetter<TMajorGetter> formLink, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter;




















    IModContext<TMod, TModGetter, TMajor, TMajorGetter> ResolveContext<TMajor, TMajorGetter>(TMajorGetter record, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter;




















    IModContext<TMod, TModGetter, TMajor, TMajorGetter> ResolveContext<TMajor, TMajorGetter>(TMajor record, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter;



















    IModContext<TMod, TModGetter, TMajor, TMajorGetter> ResolveContext<TMajor, TMajorGetter>(string editorId)
        where TMajor : class, IMajorRecordQueryable, TMajorGetter
        where TMajorGetter : class, IMajorRecordQueryableGetter;

















    IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>> ResolveAllContexts<TMajor, TMajorGetter>(FormKey formKey, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecordQueryable, TMajorGetter
        where TMajorGetter : class, IMajorRecordQueryableGetter;
















    IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> ResolveAllContexts(FormKey formKey, Type type, ResolveTarget target = ResolveTarget.Winner);















    IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> ResolveAllContexts(IFormLinkIdentifier formLink, ResolveTarget target = ResolveTarget.Winner);

















    IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>> ResolveAllContexts<TMajor, TMajorGetter>(TMajor record, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter;

















    IEnumerable<IModContext<TMod, TModGetter, TMajor, TMajorGetter>> ResolveAllContexts<TMajor, TMajorGetter>(TMajorGetter record, ResolveTarget target = ResolveTarget.Winner)
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter;












    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> ResolveAllContexts(FormKey formKey, ResolveTarget target = ResolveTarget.Winner);
}