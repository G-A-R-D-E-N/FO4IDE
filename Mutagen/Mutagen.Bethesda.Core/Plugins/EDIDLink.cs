using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Plugins;

public sealed class EDIDLink<TMajor> : IEDIDLink<TMajor>, IEquatable<IEDIDLink<TMajor>>
    where TMajor : class, IMajorRecordGetter
{

    public static readonly IEDIDLinkGetter<TMajor> Null = new EDIDLink<TMajor>();

    public RecordType EDID { get; set; }

    Type ILinkIdentifier.Type => typeof(TMajor);

    public EDIDLink()
    {
        EDID = RecordType.Null;
    }

    public EDIDLink(RecordType edid)
        : this()
    {
        EDID = edid;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not IEDIDLink<TMajor> rhs) return false;
        return Equals(rhs);
    }

    public bool Equals(IEDIDLink<TMajor>? other) => EDID.Equals(other?.EDID);

    public override int GetHashCode() => EDID.GetHashCode();

    public override string ToString() => EDID.ToString();

    private bool TryLinkToMod(
        IModGetter mod,
        [MaybeNullWhen(false)]out TMajor item)
    {
        if (EDID == RecordType.Null)
        {
            item = default;
            return false;
        }

        var group = mod.TryGetTopLevelGroup<TMajor>();
        if (group == null)
        {
            item = default;
            return false;
        }
        foreach (var rec in group)
        {
            if (EDID.Type.Equals(rec.EditorID))
            {
                item = rec;
                return true;
            }
        }
        item = default;
        return false;
    }

    public bool TryResolve(ILinkCache cache, out TMajor major)
    {
        if (EDID == RecordType.Null)
        {
            major = default!;
            return false;
        }
        foreach (var mod in cache.PriorityOrder)
        {
            if (TryLinkToMod(mod, out var item))
            {
                major = item;
                return true;
            }
        }
        major = default!;
        return false;
    }

    public bool TryResolveFormKey(ILinkCache cache, [MaybeNullWhen(false)]out FormKey formKey)
    {
        if (TryResolve(cache, out var rec))
        {
            formKey = rec.FormKey;
            return true;
        }
        formKey = default!;
        return false;
    }

    bool ILink.TryResolveCommon(ILinkCache cache, [MaybeNullWhen(false)]out IMajorRecordGetter formKey)
    {
        if (TryResolve(cache, out TMajor rec))
        {
            formKey = rec;
            return true;
        }
        formKey = default!;
        return false;
    }

    public TMajor? TryResolve(ILinkCache cache)
    {
        if (TryResolve(cache, out TMajor rec))
        {
            return rec;
        }
        return null;
    }

    bool ILink.TryGetModKey([MaybeNullWhen(false)] out ModKey modKey)
    {
        modKey = default!;
        return false;
    }

    public void SetTo(RecordType type)
    {
        EDID = type;
    }

    public void SetTo(IEDIDLinkGetter<TMajor> link)
    {
        EDID = link.EDID;
    }

    public void Clear()
    {
        EDID = RecordType.Null;
    }

    public static implicit operator EDIDLink<TMajor>(RecordType recordType)
    {
        return new EDIDLink<TMajor>(recordType);
    }

    public static implicit operator EDIDLink<TMajor>(TMajor major)
    {
        if (major.EditorID == null)
        {
            return RecordType.Null;
        }
        else
        {
            return new EDIDLink<TMajor>(new RecordType(major.EditorID));
        }
    }
}