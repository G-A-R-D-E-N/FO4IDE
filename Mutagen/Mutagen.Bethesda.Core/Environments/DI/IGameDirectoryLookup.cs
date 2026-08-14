using System.Diagnostics.CodeAnalysis;
using Noggog;

namespace Mutagen.Bethesda.Environments.DI;

public interface IGameDirectoryLookup
{





    IEnumerable<DirectoryPath> GetAll(GameRelease release);







    bool TryGet(GameRelease release, [MaybeNullWhen(false)] out DirectoryPath path);







    DirectoryPath Get(GameRelease release);






    DirectoryPath? TryGet(GameRelease release);
}

public class GameDirectoryLookupInjection : IGameDirectoryLookup
{
    private readonly GameRelease _release;
    private readonly DirectoryPath[] _path;

    public GameDirectoryLookupInjection(GameRelease release, DirectoryPath? path)
    {
        _release = release;
        if (path == null)
        {
            _path = [];
        }
        else
        {
            _path = [path.Value];
        }
    }

    private void CheckRelease(GameRelease release)
    {
        if (release != _release)
        {
            throw new ArgumentException($"Accessed a game release the inejction was not intended for: {release} != {_release}", nameof(release));
        }
    }

    public IEnumerable<DirectoryPath> GetAll(GameRelease release)
    {
        CheckRelease(release);
        return _path;
    }

    public bool TryGet(GameRelease release, out DirectoryPath path)
    {
        if (release != _release || _path.Length == 0)
        {
            path = default;
            return false;
        }

        path = _path[0];
        return true;
    }

    public DirectoryPath Get(GameRelease release)
    {
        CheckRelease(release);
        return _path[0];
    }

    public DirectoryPath? TryGet(GameRelease release)
    {
        CheckRelease(release);
        if (release != _release)
        {
            return default;
        }

        if (_path.Length == 0) return default;

        return _path[0];
    }
}