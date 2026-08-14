using Mutagen.Bethesda.Environments.DI;

namespace Mutagen.Bethesda.Archives.DI;

public interface IArchiveExtensionProvider
{

    string Get();
}

public sealed class ArchiveExtensionProvider : IArchiveExtensionProvider
{
    private readonly IGameReleaseContext _gameReleaseContext;

    public ArchiveExtensionProvider(IGameReleaseContext gameReleaseContext)
    {
        _gameReleaseContext = gameReleaseContext;
    }

    public string Get()
    {
        switch (_gameReleaseContext.Release.ToCategory())
        {
            case GameCategory.Oblivion:
            case GameCategory.Skyrim:
                return ".bsa";
            case GameCategory.Fallout4:
            case GameCategory.Starfield:
                return ".ba2";
            default:
                throw new NotImplementedException();
        }
    }
}