using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Strings;
using Mutagen.Bethesda.Strings.DI;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

public sealed class ParsingMeta
{

    public GameConstants Constants { get; }

    public IReadOnlySeparatedMasterPackage MasterReferences { get; set; }

    public RecordTypeInfoCacheReader? RecordInfoCache { get; set; }

    public ILinkCache? LinkCache { get; set; }

    public IStringsFolderLookup? StringsLookup { get; set; }

    public bool Parallel { get; set; }

    public bool InWorldspace { get; set; }

    public ushort? FormVersion { get; set; }

    public ModKey ModKey { get; }

    public EncodingBundle Encodings { get; set; } = new(MutagenEncoding._1252, MutagenEncoding._1252);

    public Language TranslatedTargetLanguage { get; set; } = Language.English;

    public bool ThrowOnUnknown { get; set; }

    public IFileSystem FileSystem { get; set; } = IFileSystemExt.DefaultFilesystem;

    public ParsingMeta(
        GameConstants constants,
        ModKey modKey,
        IReadOnlySeparatedMasterPackage masterReferences)
    {
        Constants = constants;
        ModKey = modKey;
        MasterReferences = masterReferences;
    }

    public static implicit operator GameConstants(ParsingMeta bundle)
    {
        return bundle.Constants;
    }

    public void ReportIssue(RecordType? recordType, string note)
    {

    }

    private void Absorb(StringsReadParameters? stringsReadParameters)
    {
        if (stringsReadParameters == null) return;
        if (stringsReadParameters.TargetLanguage != null)
        {
            TranslatedTargetLanguage = stringsReadParameters.TargetLanguage.Value;
        }

        if (stringsReadParameters.NonLocalizedEncodingOverride == null)
        {
            var encodingProv = stringsReadParameters.EncodingProvider ?? MutagenEncoding.Default;
            Encodings = Encodings with
            {
                NonLocalized = encodingProv.GetEncoding(Constants.Release, TranslatedTargetLanguage)
            };
        }
        else
        {
            Encodings = Encodings with
            {
                NonLocalized = stringsReadParameters.NonLocalizedEncodingOverride
            };
        }

        if (stringsReadParameters.NonTranslatedEncodingOverride != null)
        {
            Encodings = Encodings with
            {
                NonTranslated = stringsReadParameters.NonTranslatedEncodingOverride
            };
        }
    }

    private void Absorb(BinaryReadParameters? readParameters)
    {
        if (readParameters == null) return;
        if (Constants.UsesStrings)
        {
            Absorb(readParameters.StringsParam);
        }
        ThrowOnUnknown = readParameters.ThrowOnUnknownSubrecord;
        Parallel = readParameters.Parallel;
        FileSystem = readParameters.FileSystem.GetOrDefault();
        LinkCache = readParameters.LinkCache;
    }

    public static ParsingMeta Factory(
        BinaryReadParameters param,
        GameRelease release,
        ModPath modPath)
    {
        var header = ModHeaderFrame.FromPath(modPath, release, fileSystem: param.FileSystem);
        var rawMasters = param.MasterOverrides
            ?? MasterReferenceCollection.FromModHeader(modPath.ModKey, header);
        var masters = SeparatedMasterPackage.Factory(release, modPath, header.MasterStyle, rawMasters, param.MasterFlagsLookup);
        var meta = new ParsingMeta(GameConstants.Get(release), modPath.ModKey, masters);
        meta.Absorb(param);
        return meta;
    }

    public static ParsingMeta Factory(
        BinaryReadParameters param,
        GameRelease release,
        ModKey modKey,
        Stream stream)
    {
        var header = ModHeaderFrame.FromStream(stream, modKey, release);
        var rawMasters = param.MasterOverrides
            ?? MasterReferenceCollection.FromModHeader(modKey, header);
        stream.Position = 0;
        var masters = SeparatedMasterPackage.Factory(release, modKey, header.MasterStyle, rawMasters, param.MasterFlagsLookup);
        var meta = new ParsingMeta(GameConstants.Get(release), modKey, masters);
        meta.Absorb(param);
        return meta;
    }

    public static ParsingMeta FactoryNoMasters(
        BinaryReadParameters param,
        GameRelease release,
        ModKey modKey)
    {
        var meta = new ParsingMeta(GameConstants.Get(release), modKey, null!);
        meta.Absorb(param);
        return meta;
    }
}