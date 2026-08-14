using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Analysis.DI;




public interface IAutoSplitModWriter
{








    void Write<TMod, TModGetter>(
        TModGetter mod,
        FilePath path,
        BinaryWriteParameters param)
        where TMod : class, IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : class, IModGetter;
}
