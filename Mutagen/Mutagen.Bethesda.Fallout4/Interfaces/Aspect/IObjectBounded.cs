using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Fallout4;




public interface IObjectBounded : IObjectBoundedGetter, IObjectBoundedOptional
{
    new ObjectBounds ObjectBounds { get; set; }
}




public interface IObjectBoundedGetter : IObjectBoundedOptionalGetter
{
    new IObjectBoundsGetter ObjectBounds { get; }
}




public interface IObjectBoundedOptional : IObjectBoundedOptionalGetter, IMajorRecordQueryable
{
    new ObjectBounds? ObjectBounds { get; set; }
}




public interface IObjectBoundedOptionalGetter : IMajorRecordQueryableGetter
{
    IObjectBoundsGetter? ObjectBounds { get; }
}