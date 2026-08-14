using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Order;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Strings;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Binary.Parameters;

public record BinaryReadParameters
{
    public static BinaryReadParameters Default => new();

    public StringsReadParameters? StringsParam { get; init; }

    public IReadOnlyCache<IModMasterStyledGetter, ModKey>? MasterFlagsLookup { get; init; }

    public bool Parallel { get; init; } = true;

    public bool ThrowOnUnknownSubrecord { get; init; } = false;

    public IFileSystem? FileSystem { get; init; }

    public ILinkCache? LinkCache { get; init; }

    public IReadOnlyMasterReferenceCollection? MasterOverrides { get; init; }
}