using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Plugins;

public class FormLinkNullableGetter<TMajorGetter> :
    IFormLinkNullableGetter<TMajorGetter>,
    IEquatable<FormLink<TMajorGetter>>,
    IEquatable<FormLinkNullable<TMajorGetter>>,
    IEquatable<IFormLinkGetter<TMajorGetter>>,
    IEquatable<IFormLinkNullableGetter<TMajorGetter>>
    where TMajorGetter : class, IMajorRecordGetter
{
    protected FormKey? _formKey;

    public static readonly IFormLinkNullableGetter<TMajorGetter> Null = new FormLinkNullableGetter<TMajorGetter>();

    public FormKey? FormKeyNullable => _formKey;

    public FormKey FormKey => _formKey ?? FormKey.Null;

    public Type Type => typeof(TMajorGetter);

    public bool IsNull => _formKey?.IsNull ?? true;

    public FormLinkNullableGetter()
    {
    }

    public FormLinkNullableGetter(FormKey? formKey)
    {
        _formKey = formKey;
    }

    public override bool Equals(object? obj)
    {
        return IFormLinkExt.EqualsWithInheritanceConsideration(this, obj);
    }

    public bool Equals(FormLink<TMajorGetter>? other) => EqualityComparer<FormKey?>.Default.Equals(_formKey, other?.FormKey);

    public bool Equals(FormLinkNullable<TMajorGetter>? other) => EqualityComparer<FormKey?>.Default.Equals(_formKey, other?._formKey);

    public bool Equals(IFormLinkGetter<TMajorGetter>? other) => EqualityComparer<FormKey?>.Default.Equals(_formKey, other?.FormKey);

    public bool Equals(IFormLinkNullableGetter<TMajorGetter>? other) => EqualityComparer<FormKey?>.Default.Equals(_formKey, other?.FormKeyNullable);

    public override int GetHashCode() => _formKey?.GetHashCode() ?? 0;

    public override string ToString() => _formKey?.ToString() ?? "Null";

    bool ILink.TryResolveCommon(ILinkCache cache, [MaybeNullWhen(false)] out IMajorRecordGetter formKey)
    {
        if (this.TryResolve(cache, out var rec))
        {
            formKey = rec;
            return true;
        }
        formKey = default!;
        return false;
    }

    public bool TryResolveFormKey(ILinkCache cache, [MaybeNullWhen(false)] out FormKey formKey)
    {
        if (_formKey == null)
        {
            formKey = default!;
            return false;
        }
        formKey = _formKey.Value;
        return true;
    }

    public bool TryGetModKey(out ModKey modKey)
    {
        if (_formKey is {} formKey)
        {
            modKey = formKey.ModKey;
            return true;
        }
        modKey = default!;
        return false;
    }

    public TMajorGetter? TryResolve(ILinkCache cache)
    {
        if (this.TryResolve(cache, out var rec))
        {
            return rec;
        }
        return default;
    }

    IFormLinkNullable<TMajorRet> IFormLinkNullableGetter<TMajorGetter>.Cast<TMajorRet>()
    {
        return new FormLinkNullable<TMajorRet>(FormKeyNullable);
    }

    IFormLink<TMajorRet> IFormLinkGetter<TMajorGetter>.Cast<TMajorRet>()
    {
        return new FormLinkNullable<TMajorRet>(FormKeyNullable);
    }
}

public sealed class FormLinkNullable<TMajorGetter> : FormLinkNullableGetter<TMajorGetter>, IFormLinkNullable<TMajorGetter>
    where TMajorGetter : class, IMajorRecordGetter
{

    public new FormKey? FormKeyNullable
    {
        get => _formKey;
        set => _formKey = value;
    }

    public new FormKey FormKey
    {
        get => _formKey ?? FormKey.Null;
        set => _formKey = value;
    }

    public FormLinkNullable()
        : base (null)
    {
    }

    public FormLinkNullable(FormKey? formKey)
        : base(formKey)
    {
    }

    public FormLinkNullable(TMajorGetter? record)
        : base(record?.FormKey)
    {
    }

    public void SetTo(FormKey? formKey)
    {
        _formKey = formKey;
    }

    public void SetTo(TMajorGetter? record)
    {
        _formKey = record?.FormKey;
    }

    public void SetTo(IFormLinkNullableGetter<TMajorGetter> link)
    {
        _formKey = link.FormKeyNullable;
    }

    public void Clear()
    {
        _formKey = null;
    }

    public void SetToNull()
    {
        _formKey = null;
    }

    public static implicit operator FormLinkNullable<TMajorGetter>(TMajorGetter? major)
    {
        return new FormLinkNullable<TMajorGetter>(major?.FormKey);
    }

    public static implicit operator FormLinkNullable<TMajorGetter>(FormKey? formKey)
    {
        return new FormLinkNullable<TMajorGetter>(formKey);
    }

    public static implicit operator FormLinkNullable<TMajorGetter>(FormKey formKey)
    {
        return new FormLinkNullable<TMajorGetter>(formKey);
    }
}