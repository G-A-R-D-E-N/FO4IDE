using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Internals;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Meta;

public record RecordHeaderConstants
{

    public ObjectType ObjectType { get; }

    public byte HeaderLength { get; }

    public byte LengthLength { get; }

    public byte LengthAfterLength { get; }

    public byte LengthAfterType { get; }

    public byte TypeAndLengthLength { get; }

    public bool HeaderIncludedInLength { get; }

    public RecordHeaderConstants(
        ObjectType type,
        byte headerLength,
        byte lengthLength)
    {
        ObjectType = type;
        HeaderLength = headerLength;
        LengthLength = lengthLength;
        LengthAfterLength = (byte)(HeaderLength - Constants.HeaderLength - LengthLength);
        LengthAfterType = (byte)(HeaderLength - Constants.HeaderLength);
        TypeAndLengthLength = (byte)(Constants.HeaderLength + LengthLength);
        HeaderIncludedInLength = type == ObjectType.Group;
    }
}