using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Meta;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Utility;
using Mutagen.Bethesda.Strings;

namespace Mutagen.Bethesda.Plugins.Binary.Streams;

public sealed record WritingBundle(GameConstants Constants)
{

    public GameConstants Constants { get; } = Constants;

    public IReadOnlyMasterReferenceCollection? MasterReferences { get; set; }

    internal IReadOnlySeparatedMasterPackage? SeparatedMasterPackage { get; set; }

    public StringsWriter? StringsWriter { get; set; }

    public RecordTypeInfoCacheReader? RecordInfoCache { get; set; }

    public ushort? FormVersion { get; set; }

    public bool CleanNulls { get; set; } = true;

    public Language? TargetLanguageOverride { get; set; }

    public EncodingBundle Encodings { get; set; } = Constants.Encodings;

    public IModFlagsGetter? Header { get; set; }
}