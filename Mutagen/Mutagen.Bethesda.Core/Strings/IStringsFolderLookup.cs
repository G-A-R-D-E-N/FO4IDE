using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Strings;




public interface IStringsFolderLookup
{





    IReadOnlyCollection<Language> AvailableLanguages(StringsSource source);









    bool TryLookup(
        StringsSource source,
        Language language,
        uint key,
        [MaybeNullWhen(false)] out string str);








    string? Lookup(StringsSource source, Language language, uint key)
    {
        if (TryLookup(source, language, key, out var str))
        {
            return str;
        }
        return null;
    }








    TranslatedString CreateString(StringsSource source, uint key, Language targetLanguage);
}