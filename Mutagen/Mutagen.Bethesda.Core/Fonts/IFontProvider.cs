using Mutagen.Bethesda.Assets;

namespace Mutagen.Bethesda.Fonts;

public interface IFontProvider
{

    IReadOnlyList<DataRelativePath> FontLibraries { get; }

    IReadOnlyDictionary<string, FontMapping> FontMappings { get; }

    IReadOnlyList<char> ValidNameChars { get; }

    IReadOnlyList<char> ValidBookChars { get; }
}