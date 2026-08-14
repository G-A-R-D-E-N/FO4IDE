
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface IExplodeSpawn :
        IExplodeSpawnGetter,
        IFallout4MajorRecordInternal
    {
    }

    public partial interface IExplodeSpawnGetter : IFallout4MajorRecordGetter
    {
    }
}
