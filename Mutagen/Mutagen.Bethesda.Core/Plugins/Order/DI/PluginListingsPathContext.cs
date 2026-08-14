using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Installs.DI;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Order.DI;

public interface IPluginListingsPathContext
{






    FilePath Path { get; }





    FilePath? TryGetPath();
}

public sealed class PluginListingsPathContext : IPluginListingsPathContext
{
    private readonly IPluginListingsPathProvider _provider;
    private readonly IGameReleaseContext _gameReleaseContext;

    public PluginListingsPathContext(
        IPluginListingsPathProvider provider,
        IGameReleaseContext gameReleaseContext)
    {
        _provider = provider;
        _gameReleaseContext = gameReleaseContext;
    }


    public FilePath Path
    {
        get
        {
            var path = _provider.Get(_gameReleaseContext.Release);
            if (path == null)
            {
                throw new InvalidOperationException(
                    $"Could not determine plugin listings path for {_gameReleaseContext.Release}. " +
                    "This typically occurs on non-Windows platforms where the LocalAppData environment variable is not set.");
            }
            return path.Value;
        }
    }


    public FilePath? TryGetPath()
    {
        var result = _provider.Get(_gameReleaseContext.Release);
        if (result == null)
        {
            return null;
        }
        return result.Value;
    }
}

public sealed record PluginListingsPathInjection(FilePath Path) : IPluginListingsPathContext
{
    public FilePath? TryGetPath()
    {
        return Path;
    }
}
