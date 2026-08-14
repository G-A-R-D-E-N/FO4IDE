using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Archives;




public interface IArchiveReader
{






    bool TryGetFolder(string path, [MaybeNullWhen(false)] out IArchiveFolder folder);
    IEnumerable<IArchiveFile> Files { get; }
}