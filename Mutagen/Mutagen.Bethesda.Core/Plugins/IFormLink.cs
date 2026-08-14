using Loqui;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins;





public interface IFormLinkIdentifier : IFormKeyGetter, ILinkIdentifier
{
    static IFormLinkIdentifier()
    {
        Warmup.Init();
    }

    public static string GetString(IFormLinkIdentifier ident, bool simpleType = false) =>
        GetString(ident.FormKey, ident.Type, simpleType: simpleType);

    public static string GetString(FormKey formKey, Type type, bool simpleType = false)
    {
        return $"{formKey}<{GetTypeString(type, simpleType: simpleType)}>";
    }

    private static string GetTypeString(Type type, bool simpleType = false)
    {
        if (LoquiRegistration.TryGetRegister(type, out var regis))
        {
            var name = type.Name;
            if (simpleType)
            {
                name = regis.ClassType.Name;
            }

            if (regis.ClassType.Name == "MajorRecord")
            {
                return "MajorRecord";
            }
            return $"{regis.ProtocolKey.Namespace}.{name}";
        }
        else
        {
            return type.Name;
        }
    }
}





public interface IFormLinkGetter : ILink, IFormLinkIdentifier
{



    FormKey? FormKeyNullable { get; }




    bool IsNull { get; }
}





public interface IFormLinkGetter<out TMajorGetter> : ILink<TMajorGetter>, IFormLinkGetter
    where TMajorGetter : class, IMajorRecordGetter
{






    IFormLink<TMajorRet> Cast<TMajorRet>()
        where TMajorRet : class, IMajorRecordGetter;
}

public interface IFormLink<out TMajorGetter> : IFormLinkGetter<TMajorGetter>, IClearable
    where TMajorGetter : class, IMajorRecordGetter
{



    new FormKey? FormKeyNullable { get; set; }




    new FormKey FormKey { get; set; }

    void SetTo(FormKey? formKey);

    void SetToNull();
}






public interface IFormLinkNullableGetter<out TMajorGetter> :
    ILink<TMajorGetter>,
    IFormLinkGetter,
    IFormLinkGetter<TMajorGetter>
    where TMajorGetter : class, IMajorRecordGetter
{






    new IFormLinkNullable<TMajorRet> Cast<TMajorRet>()
        where TMajorRet : class, IMajorRecordGetter;
}

public interface IFormLinkNullable<out TMajorGetter> : IFormLink<TMajorGetter>, IFormLinkNullableGetter<TMajorGetter>
    where TMajorGetter : class, IMajorRecordGetter
{
}