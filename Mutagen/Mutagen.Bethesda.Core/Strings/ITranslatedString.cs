using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Strings;




public interface ITranslatedStringGetter : IEnumerable<KeyValuePair<Language, string>>
{



    Language TargetLanguage { get; }




    string? String { get; set; }








    bool TryLookup(Language language, [MaybeNullWhen(false)] out string str);

    TranslatedString DeepCopy();




    int NumLanguages { get; }
}




public interface ITranslatedString : ITranslatedStringGetter
{





    void Set(Language language, string str);






    void RemoveNonDefault(Language language);




    void ClearNonDefault();




    void Clear();




    new string? String { get; set; }
}

public static class TranslatedStringExt
{







    public static string? Lookup(this ITranslatedStringGetter getter, Language language)
    {
        if (getter.TryLookup(language, out var str))
        {
            return str;
        }
        if (language == getter.TargetLanguage) return string.Empty;
        return null;
    }
}