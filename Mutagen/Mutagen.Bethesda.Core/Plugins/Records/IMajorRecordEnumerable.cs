namespace Mutagen.Bethesda.Plugins.Records;

public interface IMajorRecordEnumerable : IMajorRecordGetterEnumerable
{

    new IEnumerable<IMajorRecord> EnumerateMajorRecords();

    new IEnumerable<TMajor> EnumerateMajorRecords<TMajor>(bool throwIfUnknown = true)
        where TMajor : class, IMajorRecordQueryable;

    new IEnumerable<IMajorRecord> EnumerateMajorRecords(Type? t, bool throwIfUnknown = true);

    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    void Remove(FormKey formKey);

    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    void Remove(IEnumerable<FormKey> formKeys);

    [Obsolete("This call is not as optimized as its generic typed counterpart.  Use as a last resort.")]
    void Remove(HashSet<FormKey> formKeys);

    void Remove(IEnumerable<IFormLinkIdentifier> formLinks);

    void Remove(FormKey formKey, Type type, bool throwIfUnknown = true);

    void Remove(IEnumerable<FormKey> formKeys, Type type, bool throwIfUnknown = true);

    void Remove(HashSet<FormKey> formKeys, Type type, bool throwIfUnknown = true);

    void Remove<TMajor>(FormKey formKey, bool throwIfUnknown = true)
        where TMajor : IMajorRecordGetter;

    void Remove<TMajor>(HashSet<FormKey> formKeys, bool throwIfUnknown = true)
        where TMajor : IMajorRecordGetter;

    void Remove<TMajor>(IEnumerable<FormKey> formKeys, bool throwIfUnknown = true)
        where TMajor : IMajorRecordGetter;

    void Remove<TMajor>(TMajor record, bool throwIfUnknown = true)
        where TMajor : IMajorRecordGetter;

    void Remove<TMajor>(IEnumerable<TMajor> records, bool throwIfUnknown = true)
        where TMajor : IMajorRecordGetter;
}

public interface IMajorRecordGetterEnumerable
{

    IEnumerable<IMajorRecordGetter> EnumerateMajorRecords();

    IEnumerable<T> EnumerateMajorRecords<T>(bool throwIfUnknown = true)
        where T : class, IMajorRecordQueryableGetter;

    IEnumerable<IMajorRecordGetter> EnumerateMajorRecords(Type type, bool throwIfUnknown = true);
}