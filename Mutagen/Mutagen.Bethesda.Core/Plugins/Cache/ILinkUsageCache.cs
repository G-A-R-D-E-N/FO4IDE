using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Cache;

public interface ILinkUsageResults<TScope>
    where TScope : class, IMajorRecordGetter
{
    bool Contains(FormKey formKey);
    bool Contains(IFormLinkIdentifier identifier);
    bool Contains(IFormLinkGetter<TScope> link);
    bool Contains(TScope record);
    IReadOnlySet<IFormLinkGetter<TScope>> UsageLinks { get; }
}

public interface ILinkUsageCache
{






    ILinkUsageResults<TUserRecordScope> GetUsagesOf<TUserRecordScope>(
        IFormLinkIdentifier identifier)
        where TUserRecordScope : class, IMajorRecordGetter;






    ILinkUsageResults<IMajorRecordGetter> GetUsagesOf(
        IFormLinkIdentifier identifier);







    ILinkUsageResults<TUserRecordScope> GetUsagesOf<TUserRecordScope>(
        IMajorRecordGetter majorRecord)
        where TUserRecordScope : class, IMajorRecordGetter;






    ILinkUsageResults<IMajorRecordGetter> GetUsagesOf(
        IMajorRecordGetter majorRecord);






    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    ILinkUsageResults<IMajorRecordGetter> GetUsagesOf(FormKey formKey);
}