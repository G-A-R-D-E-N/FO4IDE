using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Fallout4;

public interface IModeled : IModeledGetter, IMajorRecordQueryable
{
    new Model? Model { get; set; }
}

public interface IModeledGetter : IMajorRecordQueryableGetter
{
    IModelGetter? Model { get; }
}