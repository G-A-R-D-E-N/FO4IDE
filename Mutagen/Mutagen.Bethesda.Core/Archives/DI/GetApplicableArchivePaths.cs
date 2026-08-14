using System.IO.Abstractions;
using Mutagen.Bethesda.Environments.DI;
using Mutagen.Bethesda.Plugins;
using Noggog;

namespace Mutagen.Bethesda.Archives.DI;

public interface IGetApplicableArchivePaths
{

    IEnumerable<FilePath> Get();

    IEnumerable<FilePath> Get(ModKey modKey);
}

public sealed class GetApplicableArchivePaths : IGetApplicableArchivePaths
{
    private readonly IFileSystem _fileSystem;
    private readonly ICheckArchiveApplicability _applicability;
    private readonly IDataDirectoryProvider _dataDirectoryProvider;
    private readonly IArchiveExtensionProvider _archiveExtension;
    private readonly IArchiveListingDetailsProvider _archiveListingDetailsProvider;

    public GetApplicableArchivePaths(
        IFileSystem fileSystem,
        ICheckArchiveApplicability applicability,
        IDataDirectoryProvider dataDirectoryProvider,
        IArchiveExtensionProvider archiveExtension,
        IArchiveListingDetailsProvider archiveListingDetailsProvider)
    {
        _fileSystem = fileSystem;
        _applicability = applicability;
        _dataDirectoryProvider = dataDirectoryProvider;
        _archiveExtension = archiveExtension;
        _archiveListingDetailsProvider = archiveListingDetailsProvider;
    }

    public IEnumerable<FilePath> Get()
    {
        return GetInternal(default(ModKey?));
    }

    public IEnumerable<FilePath> Get(ModKey modKey)
    {
        return GetInternal(modKey);
    }

    private IEnumerable<FilePath> GetInternal(ModKey? modKey)
    {
        if (modKey.HasValue && modKey.Value.IsNull)
        {
            return [];
        }

        if (!_fileSystem.Directory.Exists(_dataDirectoryProvider.Path))
        {
            return [];
        }

        var ret = _fileSystem.Directory
            .EnumerateFilePaths(_dataDirectoryProvider.Path, searchPattern: $"*{_archiveExtension.Get()}");
        if (modKey != null && !modKey.Value.IsNull)
        {
            ret = ret
                .Where(x => _archiveListingDetailsProvider.IsIni(x.Name) || _applicability.IsApplicable(modKey.Value, x.Name));
        }
        else
        {
            ret = ret
                .Where(x => _archiveListingDetailsProvider.Contains(x.Name));
        }
        return ret.OrderBy(x => x.Name, _archiveListingDetailsProvider.GetComparerFor(modKey));
    }
}