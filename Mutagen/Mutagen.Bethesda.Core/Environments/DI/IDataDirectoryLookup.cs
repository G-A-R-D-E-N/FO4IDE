using System.Diagnostics.CodeAnalysis;
using Noggog;

namespace Mutagen.Bethesda.Environments.DI;

public interface IDataDirectoryLookup
{





    IEnumerable<DirectoryPath> GetAll(GameRelease release);







    bool TryGet(GameRelease release, [MaybeNullWhen(false)] out DirectoryPath path);







    DirectoryPath Get(GameRelease release);
}