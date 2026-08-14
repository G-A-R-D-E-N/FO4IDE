using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Aspects;

public interface IKeywordCommon : IKeywordCommonGetter, IMajorRecord
{
}

public interface IKeywordCommonGetter : IMajorRecordGetter
{
}