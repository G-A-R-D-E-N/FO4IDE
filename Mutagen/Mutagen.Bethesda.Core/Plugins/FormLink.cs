using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Plugins;






[DebuggerDisplay("{this.FormKey}")]
public class FormLinkGetter<TMajorGetter> : IFormLinkGetter<TMajorGetter>,
    IEquatable<FormLink<TMajorGetter>>,
    IEquatable<FormLinkNullable<TMajorGetter>>,
    IEquatable<IFormLinkGetter<TMajorGetter>>,
    IEquatable<IFormLinkNullableGetter<TMajorGetter>>,
    IEquatable<TMajorGetter>
    where TMajorGetter : class, IMajorRecordGetter
{
    protected FormKey _formKey;




    public static readonly IFormLinkGetter<TMajorGetter> Null = new FormLinkGetter<TMajorGetter>();




    public FormKey FormKey => _formKey;




    public bool IsNull => FormKey.IsNull;


    public Type Type => typeof(TMajorGetter);

    FormKey? IFormLinkGetter.FormKeyNullable => FormKey;

    public FormLinkGetter()
    {
        _formKey = FormKey.Null;
    }

    public FormLinkGetter(FormKey formKey)
    {
        _formKey = formKey;
    }

    public bool TryResolveFormKey(ILinkCache cache, [MaybeNullWhen(false)] out FormKey formKey)
    {
        formKey = FormKey;
        return true;
    }

    public bool TryResolveCommon(ILinkCache cache, [MaybeNullWhen(false)] out IMajorRecordGetter formKey)
    {
        if (this.TryResolve(cache, out var rec))
        {
            formKey = rec;
            return true;
        }
        formKey = default;
        return false;
    }

    public bool TryGetModKey([MaybeNullWhen(false)] out ModKey modKey)
    {
        modKey = FormKey.ModKey;
        return true;
    }






    public TMajorGetter? TryResolve(ILinkCache cache)
    {
        if (this.TryResolve(cache, out var rec))
        {
            return rec;
        }
        return default;
    }






    public override bool Equals(object? obj)
    {
        return IFormLinkExt.EqualsWithInheritanceConsideration(this, obj);
    }






    public bool Equals(FormLink<TMajorGetter>? other) => FormKey.Equals(other?.FormKey ?? FormKey.Null);






    public bool Equals(FormLinkNullable<TMajorGetter>? other) => EqualityComparer<FormKey?>.Default.Equals(FormKey, other?.FormKeyNullable);






    public bool Equals(IFormLinkGetter<TMajorGetter>? other) => FormKey.Equals(other?.FormKey);






    public bool Equals(IFormLinkNullableGetter<TMajorGetter>? other) => EqualityComparer<FormKey?>.Default.Equals(FormKey, other?.FormKeyNullable);





    public override int GetHashCode() => FormKey.GetHashCode();





    public override string ToString() => $"{FormKey}<{MajorRecordPrinter<TMajorGetter>.TypeString}>";

    public bool Equals(TMajorGetter? other)
    {
        return IFormLinkExt.EqualsWithInheritanceConsideration(this, other);
    }

    public IFormLink<TMajorRet> Cast<TMajorRet>()
        where TMajorRet : class, IMajorRecordGetter
    {
        return new FormLink<TMajorRet>(FormKey);
    }

    public static IEqualityComparer<IFormLinkGetter<TMajorGetter>> TypelessComparer => FormLinkTypelessComparer<TMajorGetter>.Instance;
}






[DebuggerDisplay("{this.FormKey}")]
public sealed class FormLink<TMajorGetter> : FormLinkGetter<TMajorGetter>, IFormLink<TMajorGetter>
    where TMajorGetter : class, IMajorRecordGetter
{



    public new FormKey FormKey
    {
        get => _formKey;
        set => _formKey = value;
    }

    FormKey? IFormLink<TMajorGetter>.FormKeyNullable
    {
        get => FormKey;
        set => FormKey = value ?? FormKey.Null;
    }

    public FormLink()
        : base(FormKey.Null)
    {
    }




    public FormLink(FormKey formKey)
        : base(formKey)
    {
    }




    public FormLink(TMajorGetter record)
        : base(record.FormKey)
    {
    }





    public void SetTo(FormKey? formKey)
    {
        FormKey = formKey ?? FormKey.Null;
    }





    public void SetTo(TMajorGetter? record)
    {
        FormKey = record?.FormKey ?? FormKey.Null;
    }





    public void SetTo(IFormLinkNullableGetter<TMajorGetter> link)
    {
        FormKey = link.FormKey;
    }

    public void SetToNull()
    {
        _formKey = FormKey.Null;
    }

    public void Clear()
    {
        FormKey = FormKey.Null;
    }

    public static implicit operator FormLink<TMajorGetter>(TMajorGetter major)
    {
        return new FormLink<TMajorGetter>(major.FormKey);
    }

    public static implicit operator FormLink<TMajorGetter>(FormKey formKey)
    {
        return new FormLink<TMajorGetter>(formKey);
    }
}