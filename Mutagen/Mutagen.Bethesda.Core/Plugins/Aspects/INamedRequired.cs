using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Aspects;




public interface INamedRequired : INamedRequiredGetter, IMajorRecordQueryable
{



    new String Name { get; set; }
}




public interface INamedRequiredGetter : IMajorRecordQueryableGetter
{



    String Name { get; }
}