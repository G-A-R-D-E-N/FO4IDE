
using Mutagen.Bethesda;

namespace Mutagen.Bethesda.Fallout4
{

    public partial interface INpcSpawn :
        IFallout4MajorRecordInternal,
        INpcSpawnGetter
    {
    }

    public partial interface INpcSpawnGetter : IFallout4MajorRecordGetter
    {
    }
}
