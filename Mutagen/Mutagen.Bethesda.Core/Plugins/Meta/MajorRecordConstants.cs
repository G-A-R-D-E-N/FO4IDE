namespace Mutagen.Bethesda.Plugins.Meta;

public sealed record MajorRecordConstants : RecordHeaderConstants
{

    public byte FlagLocationOffset { get; }

    public byte FormIDLocationOffset { get; }

    public byte? FormVersionLocationOffset { get; }

    public MajorRecordConstants(
        byte headerLength,
        byte lengthLength,
        byte flagsLoc,
        byte formIDloc,
        byte? formVersionLoc)
        : base(ObjectType.Record, headerLength, lengthLength)
    {
        FlagLocationOffset = flagsLoc;
        FormIDLocationOffset = formIDloc;
        FormVersionLocationOffset = formVersionLoc;
    }
}