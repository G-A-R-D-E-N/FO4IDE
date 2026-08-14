using Mutagen.Bethesda.Assets;
using Mutagen.Bethesda.Strings;

namespace Mutagen.Bethesda.Fonts.DI;

public class FontProvider : IFontProvider
{
	private readonly List<DataRelativePath> _fontLibraries = new();
	private readonly Dictionary<string, FontMapping> _fontMappings = new();
	private char[] _validNameChars = [];
	private char[] _validBookChars = [];

	public IReadOnlyList<DataRelativePath> FontLibraries => _fontLibraries;
	public IReadOnlyDictionary<string, FontMapping> FontMappings => _fontMappings;
	public IReadOnlyList<char> ValidNameChars => _validNameChars;
	public IReadOnlyList<char> ValidBookChars => _validBookChars;

	public FontProvider(
		Language language,
		IGetFontConfig fontConfig)
	{
		using var configFileStream = fontConfig.GetStream(language);
		Init(configFileStream);
	}

	private void Init(Stream stream)
	{
		var reader = new StreamReader(stream);
		while (!reader.EndOfStream)
		{
			var line = reader.ReadLine();
			if (line is null) break;

			if (line.StartsWith("map", StringComparison.OrdinalIgnoreCase))
			{

				var span = line.AsSpan();

				span = span[5..];

				var aliasEnd = span.IndexOf('"');
				var alias = span[..aliasEnd].ToString();

				span = span[(aliasEnd + 5)..];

				var fonIdEnd = span.IndexOf('"');
				var fontId = span[..fonIdEnd].ToString();

				span = span[(fonIdEnd + 2)..];

				var fontWeight = span.ToString();

				_fontMappings.Add(alias, new FontMapping(fontId, fontWeight));
			}
			else if (line.StartsWith("fontlib", StringComparison.OrdinalIgnoreCase))
			{

				var lib = line[9..^1];

				if (!lib.StartsWith("Interface", StringComparison.OrdinalIgnoreCase))
				{
					lib = @"Interface\" + lib;
				}

				_fontLibraries.Add(lib);
			}
			else if (line.StartsWith("validBookChars", StringComparison.OrdinalIgnoreCase))
			{

				_validBookChars = line[16..^1].ToCharArray();
			}
			else if (line.StartsWith("validNameChars", StringComparison.OrdinalIgnoreCase))
			{

				_validNameChars = line[16..^1].ToCharArray();
			}
		}
	}
}
