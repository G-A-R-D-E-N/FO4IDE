using Mutagen.Bethesda.Plugins;
using Noggog;

namespace Mutagen.Bethesda.Archives.DI;

public interface ICheckArchiveApplicability
{










    bool IsApplicable(ModKey modKey, FileName archiveFileName);
}

public sealed class CheckArchiveApplicability : ICheckArchiveApplicability
{
    private readonly IArchiveExtensionProvider _archiveExtensionProvider;

    public CheckArchiveApplicability(IArchiveExtensionProvider archiveExtensionProvider)
    {
        _archiveExtensionProvider = archiveExtensionProvider;
    }


    public bool IsApplicable(ModKey modKey, FileName archiveFileName)
    {
        if (!archiveFileName.Extension.Equals(_archiveExtensionProvider.Get(), StringComparison.OrdinalIgnoreCase)) return false;
        var nameWithoutExt = archiveFileName.NameWithoutExtension.AsSpan();


        if (modKey.Name.AsSpan().Equals(nameWithoutExt, StringComparison.OrdinalIgnoreCase)) return true;


        var delimIndex = nameWithoutExt.LastIndexOf(" - ");
        if (delimIndex == -1) return false;

        return modKey.Name.AsSpan().Equals(nameWithoutExt.Slice(0, delimIndex), StringComparison.OrdinalIgnoreCase);
    }
}