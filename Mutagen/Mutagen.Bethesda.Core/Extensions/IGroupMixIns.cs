using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;
using Noggog;
using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda;

public static class IGroupMixIns
{

    public static TMajor AddNew<TMajor>(this IGroup<TMajor> group, FormKey formKey)
        where TMajor : IMajorRecord
    {
        var ret = MajorRecordInstantiator<TMajor>.Activator(
            formKey,
            group.SourceMod.GameRelease);
        group.Set(ret);
        return ret;
    }

    public static IMajorRecord AddNew(this IGroup group, FormKey formKey)
    {
        var ret = MajorRecordInstantiator.Activator(
            formKey,
            group.SourceMod.GameRelease,
            group.ContainedRecordRegistration.ClassType);
        group.SetUntyped(ret);
        return ret;
    }

    public static TMajor AddNew<TMajor>(this IGroup<TMajor> group)
        where TMajor : IMajorRecord
    {
        var ret = MajorRecordInstantiator<TMajor>.Activator(
            group.SourceMod.GetNextFormKey(),
            group.SourceMod.GameRelease);
        group.Set(ret);
        return ret;
    }

    public static IMajorRecord AddNew(this IGroup group)
    {
        var ret = MajorRecordInstantiator.Activator(
            group.SourceMod.GetNextFormKey(),
            group.SourceMod.GameRelease,
            group.ContainedRecordRegistration.ClassType);
        group.SetUntyped(ret);
        return ret;
    }

    public static TMajor AddNew<TMajor>(this IGroup<TMajor> group, string? editorID)
        where TMajor : IMajorRecord
    {
        var ret = MajorRecordInstantiator<TMajor>.Activator(
            group.SourceMod.GetNextFormKey(editorID),
            group.SourceMod.GameRelease);
        ret.EditorID = editorID;
        group.Set(ret);
        return ret;
    }

    public static IMajorRecord AddNew(this IGroup group, string? editorID)
    {
        var ret = MajorRecordInstantiator.Activator(
            group.SourceMod.GetNextFormKey(editorID),
            group.SourceMod.GameRelease,
            group.ContainedRecordRegistration.ClassType);
        ret.EditorID = editorID;
        group.SetUntyped(ret);
        return ret;
    }

    public static TMajor DuplicateInAsNewRecord<TMajor, TMajorGetter>(this IGroup<TMajor> group, TMajorGetter source)
        where TMajor : class, IMajorRecord, TMajorGetter
        where TMajorGetter : IMajorRecordGetter
    {
        return DuplicateInAsNewRecord<TMajor, TMajorGetter, TMajorGetter>(group, source);
    }

    public static IMajorRecord DuplicateInAsNewUntypedRecord(this IGroup group, IMajorRecord source)
    {
        try
        {
            var newRec = source.Duplicate(group.SourceMod.GetNextFormKey());
            group.AddUntyped(newRec);
            return newRec;
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow(ex, source.FormKey, group.ContainedRecordRegistration.ClassType, source.EditorID);
            throw;
        }
    }

    public static TMajor DuplicateInAsNewRecord<TMajor, TMajorGetter, TSharedParent>(this IGroup<TMajor> group, TMajorGetter source)
        where TMajor : class, IMajorRecord, TSharedParent
        where TMajorGetter : TSharedParent
        where TSharedParent : IMajorRecordGetter
    {
        try
        {
            var newRec = (source.Duplicate(group.SourceMod.GetNextFormKey()) as TMajor)!;
            group.Add(newRec);
            return newRec;
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow<TMajor>(ex, source.FormKey, source.EditorID);
            throw;
        }
    }

    public static TMajor DuplicateInAsNewRecord<TMajor, TMajorGetter>(this IGroup<TMajor> group, TMajorGetter source, string? edid, FormKey? formKey = null)
        where TMajor : IMajorRecord, TMajorGetter
        where TMajorGetter : IMajorRecordGetter
    {
        return DuplicateInAsNewRecord<TMajor, TMajorGetter, TMajorGetter>(group, source, edid, formKey);
    }

    public static TMajor DuplicateInAsNewRecord<TMajor, TMajorGetter>(this IGroup<TMajor> group, TMajorGetter source, FormKey? formKey)
        where TMajor : IMajorRecord, TMajorGetter
        where TMajorGetter : IMajorRecordGetter
    {
        return DuplicateInAsNewRecord<TMajor, TMajorGetter, TMajorGetter>(group, source, formKey);
    }

    public static TMajor DuplicateInAsNewRecord<TMajor, TMajorGetter, TSharedParent>(this IGroup<TMajor> group, TMajorGetter source, string? edid, FormKey? formKey = null)
        where TMajor : IMajorRecord, TSharedParent
        where TMajorGetter : TSharedParent
        where TSharedParent : IMajorRecordGetter
    {
        try
        {
            var dup = source.Duplicate(formKey ?? group.SourceMod.GetNextFormKey(edid));
            if (dup is not TMajor newRec)
            {
                throw new InvalidOperationException($"Duplicate did not return a record of the expected type {typeof(TMajor).Name}");
            }
            if (edid != null)
            {
                dup.EditorID = edid;
            }
            group.Add(newRec);
            return newRec;
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow<TMajor>(ex, source.FormKey, source.EditorID);
            throw;
        }
    }

    public static TMajor DuplicateInAsNewRecord<TMajor, TMajorGetter, TSharedParent>(this IGroup<TMajor> group, TMajorGetter source, FormKey? formKey)
        where TMajor : IMajorRecord, TSharedParent
        where TMajorGetter : TSharedParent
        where TSharedParent : IMajorRecordGetter
    {
        return DuplicateInAsNewRecord<TMajor, TMajorGetter, TSharedParent>(group, source, default(string?), formKey);
    }

    public static IMajorRecord DuplicateInAsNewUntypedRecord(this IGroup group, IMajorRecord source, string? edid, FormKey? formKey = null)
    {
        try
        {
            var newRec = source.Duplicate(formKey ?? group.SourceMod.GetNextFormKey(edid));
            if (edid != null)
            {
                newRec.EditorID = edid;
            }
            group.AddUntyped(newRec);
            return newRec;
        }
        catch (Exception ex)
        {
            RecordException.EnrichAndThrow(ex, source.FormKey, group.ContainedRecordRegistration.ClassType, source.EditorID);
            throw;
        }
    }

    public static IMajorRecord DuplicateInAsNewUntypedRecord(this IGroup group, IMajorRecord source, FormKey? formKey)
    {
        return DuplicateInAsNewUntypedRecord(group, source, default(string?), formKey);
    }

    public static bool TryGetValue<TMajor>(
        this IGroupGetter<TMajor> group,
        FormKey formKey,
        [MaybeNullWhen(false)] out TMajor record)
        where TMajor : IMajorRecordGetter
    {
        return group.RecordCache.TryGetValue(formKey, out record);
    }

    public static TMajor? TryGetValue<TMajor>(
        this IGroupGetter<TMajor> group,
        FormKey formKey)
        where TMajor : IMajorRecordGetter
    {
        if (group.RecordCache.TryGetValue(formKey, out var record))
        {
            return record;
        }
        return default;
    }
}