using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins;




public interface IEDIDLinkGetter : ILink
{



    RecordType EDID { get; }
}

public interface IEDIDLink : IEDIDLinkGetter
{



    new RecordType EDID { get; set; }
}





public interface IEDIDLinkGetter<out TMajor> : ILink<TMajor>, IEDIDLinkGetter
    where TMajor : IMajorRecordGetter
{
}

public interface IEDIDLink<TMajor> : IEDIDLinkGetter<TMajor>, IEDIDLink, IClearable
    where TMajor : IMajorRecordGetter
{
    void SetTo(RecordType type);
    void SetTo(IEDIDLinkGetter<TMajor> rhsLink);
}




public static class IEDIDLinkExt
{



    public static IEDIDLink<TMajor> AsSetter<TMajor>(this IEDIDLinkGetter<TMajor> link)
        where TMajor : class, IMajorRecordGetter
    {
        return new EDIDLink<TMajor>(link.EDID);
    }
}