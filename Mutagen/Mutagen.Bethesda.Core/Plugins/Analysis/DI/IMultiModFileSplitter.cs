using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Analysis.DI;




public interface IMultiModFileSplitter
{








    IReadOnlyList<TMod> Split<TMod, TModGetter>(TMod inputMod, int masterLimit)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter;
}
