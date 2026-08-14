using Loqui;
using Mutagen.Bethesda.Plugins.Cache;

namespace Mutagen.Bethesda.Plugins.Records;

public interface IMajorRecordSimpleContextEnumerable
{




    IEnumerable<IModContext<IMajorRecordGetter>> EnumerateMajorRecordSimpleContexts();







    IEnumerable<IModContext<TMajor>> EnumerateMajorRecordSimpleContexts<TMajor>(bool throwIfUnknown = true)
        where TMajor : class, IMajorRecordQueryableGetter;








    IEnumerable<IModContext<IMajorRecordGetter>> EnumerateMajorRecordSimpleContexts(Type t, bool throwIfUnknown = true);
}

public interface IMajorRecordContextEnumerable<TMod, TModGetter> : IMajorRecordSimpleContextEnumerable
    where TModGetter : IModGetter
    where TMod : TModGetter, IMod
{







    IEnumerable<IModContext<TMod, TModGetter, TSetter, TGetter>> EnumerateMajorRecordContexts<TSetter, TGetter>(ILinkCache linkCache, bool throwIfUnknown = true)
        where TSetter : class, IMajorRecordQueryable, TGetter
        where TGetter : class, IMajorRecordQueryableGetter;









    IEnumerable<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> EnumerateMajorRecordContexts(ILinkCache linkCache, Type t, bool throwIfUnknown = true);
}

internal static class MajorRecordContextEnumerableUtility
{
    internal enum TypeMatch
    {
        NotMatch,
        MajorRecord,
        Match
    }

    static MajorRecordContextEnumerableUtility()
    {
        Warmup.Init();
    }

    public static TypeMatch GetMatch(Type type, string fullName)
    {
        if (!LoquiRegistration.TryGetRegister(type, out var regis)) return TypeMatch.NotMatch;
        if (regis.Name.Equals("MajorRecord")) return TypeMatch.MajorRecord;
        return regis.FullName.Equals(fullName) ? TypeMatch.Match : TypeMatch.NotMatch;
    }
}