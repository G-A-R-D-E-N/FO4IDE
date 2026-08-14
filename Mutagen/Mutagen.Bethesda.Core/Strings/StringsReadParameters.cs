using Noggog;
using Mutagen.Bethesda.Strings.DI;

namespace Mutagen.Bethesda.Strings;




public sealed record StringsReadParameters
{



    public DirectoryPath? StringsFolderOverride { get; init; }




    public DirectoryPath? BsaFolderOverride { get; init; }




    public IMutagenEncodingProvider? EncodingProvider { get; init; }






    public Language? TargetLanguage { get; init; }




    public IMutagenEncoding? NonTranslatedEncodingOverride { get; init; }




    public IMutagenEncoding? NonLocalizedEncodingOverride { get; init; }
}