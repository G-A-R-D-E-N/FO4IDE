using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Meta;
using Noggog;

namespace Mutagen.Bethesda;




public static class StreamHeaderMixIn
{
    #region Normal Stream











    public static ModHeader GetModHeader<TStream>(this TStream stream, GameConstants constants, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        var initialPos = stream.Position;
        var remaining = stream.Remaining;
        try
        {
            return new ModHeader(constants, stream.GetMemory(constants.ModHeaderLength, readSafe: readSafe));
        }
        catch (ArgumentException)
        {
            throw new MalformedDataException($"Could not read enough data to parse a Mod Header from stream.  Position: {initialPos}.  {remaining} remaining < {constants.ModHeaderLength} expected.");
        }
        catch (EndOfStreamException)
        {
            throw new MalformedDataException($"Could not read enough data to parse a Mod Header from stream.  Position: {initialPos}.  {remaining} remaining < {constants.ModHeaderLength} expected.");
        }
    }












    public static ModHeader ReadModHeader<TStream>(this TStream stream, GameConstants constants, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        var initialPos = stream.Position;
        var remaining = stream.Remaining;
        try
        {
            return new ModHeader(constants, stream.ReadMemory(constants.ModHeaderLength, readSafe: readSafe));
        }
        catch (ArgumentException)
        {
            throw new MalformedDataException($"Could not read enough data to parse a Mod Header from stream.  Position: {initialPos}.  {remaining} remaining < {constants.ModHeaderLength} expected.");
        }
        catch (EndOfStreamException)
        {
            throw new MalformedDataException($"Could not read enough data to parse a Mod Header from stream.  Position: {initialPos}.  {remaining} remaining < {constants.ModHeaderLength} expected.");
        }
    }













    public static bool TryGetModHeader<TStream>(this TStream stream, GameConstants constants, out ModHeader header, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.ModHeaderLength)
        {
            header = default;
            return false;
        }
        header = new ModHeader(constants, stream.ReadMemory(constants.ModHeaderLength, readSafe: readSafe));
        return true;
    }













    public static bool TryReadModHeader<TStream>(this TStream stream, GameConstants constants, out ModHeader header, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.ModHeaderLength)
        {
            header = default;
            return false;
        }
        header = new ModHeader(constants, stream.ReadMemory(constants.ModHeaderLength, readSafe: readSafe));
        return true;
    }











    public static ModHeaderFrame GetModHeaderFrame<TStream>(this TStream stream, GameConstants constants, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        var initialPos = stream.Position;
        var remaining = stream.Remaining;
        int? expected = null;
        try
        {
            var meta = GetModHeader(stream, constants, readSafe: readSafe);
            return new ModHeaderFrame(meta, stream.GetMemory(checked((int)meta.TotalLength), readSafe: readSafe));
        }
        catch (ArgumentException)
        {
            throw new MalformedDataException($"Could not read enough data to parse a Mod Header Frame from stream.  Position: {initialPos}.  {remaining} remaining < {expected ?? constants.ModHeaderLength} expected.");
        }
        catch (EndOfStreamException)
        {
            throw new MalformedDataException($"Could not read enough data to parse a Mod Header Frame from stream.  Position: {initialPos}.  {remaining} remaining < {expected ?? constants.ModHeaderLength} expected.");
        }
    }












    public static ModHeaderFrame ReadModHeaderFrame<TStream>(this TStream stream, GameConstants constants, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        var initialPos = stream.Position;
        var remaining = stream.Remaining;
        int? expected = null;
        try
        {
            var meta = GetModHeader(stream, constants, readSafe: readSafe);
            expected = checked((int)meta.TotalLength);
            return new ModHeaderFrame(meta, stream.ReadMemory(checked((int)meta.TotalLength), readSafe: readSafe));
        }
        catch (ArgumentException)
        {
            throw new MalformedDataException($"Could not read enough data to parse a Mod Header Frame from stream.  Position: {initialPos}.  {remaining} remaining < {expected ?? constants.ModHeaderLength} expected.");
        }
        catch (EndOfStreamException)
        {
            throw new MalformedDataException($"Could not read enough data to parse a Mod Header Frame from stream.  Position: {initialPos}.  {remaining} remaining < {expected ?? constants.ModHeaderLength} expected.");
        }
    }













    public static bool TryGetModHeaderFrame<TStream>(this TStream stream, GameConstants constants, out ModHeaderFrame frame, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (!TryGetModHeader(stream, constants, out var meta, readSafe: false))
        {
            frame = default;
            return false;
        }
        frame = new ModHeaderFrame(meta, stream.GetMemory(checked((int)meta.TotalLength), readSafe: readSafe));
        return true;
    }













    public static bool TryReadModHeaderFrame<TStream>(this TStream stream, GameConstants constants, out ModHeaderFrame frame, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (!TryGetModHeader(stream, constants, out var meta, readSafe: false))
        {
            frame = default;
            return false;
        }
        frame = new ModHeaderFrame(meta, stream.ReadMemory(checked((int)meta.TotalLength), readSafe: readSafe));
        return true;
    }















    public static GroupPinHeader GetGroupHeader<TStream>(this TStream stream, GameConstants constants, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IBinaryReadStream
    {
        var ret = new GroupPinHeader(constants, stream.GetMemory(constants.GroupConstants.HeaderLength, offset, readSafe: readSafe), pinLocation: stream.Position);
        if (checkIsGroup && !ret.IsGroup)
        {
            throw new MalformedDataException("Read in data that was not a GRUP");
        }
        return ret;
    }















    public static GroupPinHeader ReadGroupHeader<TStream>(this TStream stream, GameConstants constants, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IBinaryReadStream
    {
        var loc = stream.Position;
        var ret = new GroupPinHeader(constants, stream.ReadMemory(constants.GroupConstants.HeaderLength, offset, readSafe: readSafe), pinLocation: loc);
        if (checkIsGroup && !ret.IsGroup)
        {
            throw new MalformedDataException("Read in data that was not a GRUP");
        }
        return ret;
    }















    public static bool TryGetGroupHeader<TStream>(this TStream stream, GameConstants constants, out GroupPinHeader header, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.GroupConstants.HeaderLength + offset)
        {
            header = default;
            return false;
        }
        header = GetGroupHeader(stream, constants, offset: offset, readSafe: readSafe, checkIsGroup: false);
        return !checkIsGroup || header.IsGroup;
    }















    public static GroupPinFrame GetGroup<TStream>(this TStream stream, GameConstants constants, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IBinaryReadStream
    {
        var meta = GetGroupHeader(stream, constants, offset: offset, readSafe: readSafe, checkIsGroup: checkIsGroup);
        return new GroupPinFrame(
            new GroupFrame(meta, stream.GetMemory(checked((int)meta.TotalLength), offset: offset, readSafe: readSafe)),
            pinLocation: stream.Position);
    }















    public static bool TryGetGroup<TStream>(this TStream stream, GameConstants constants, out GroupPinFrame frame, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IBinaryReadStream
    {
        if (!TryGetGroupHeader(stream, constants, out var meta, offset: offset, checkIsGroup: checkIsGroup, readSafe: false))
        {
            frame = default;
            return false;
        }
        frame = new GroupPinFrame(
            new GroupFrame(meta, stream.GetMemory(checked((int)meta.TotalLength), readSafe: readSafe)),
            pinLocation: stream.Position);
        return true;
    }















    public static bool TryReadGroupHeader<TStream>(this TStream stream, GameConstants constants, out GroupPinHeader header, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.GroupConstants.HeaderLength)
        {
            header = default;
            return false;
        }
        header = ReadGroupHeader(stream, constants, offset: offset, readSafe: readSafe, checkIsGroup: false);
        var ret = !checkIsGroup || header.IsGroup;
        if (!ret)
        {
            stream.Position -= header.HeaderLength;
        }
        return ret;
    }














    public static GroupPinFrame ReadGroup<TStream>(this TStream stream, GameConstants constants, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IBinaryReadStream
    {
        var loc = stream.Position;
        var meta = GetGroupHeader(stream, constants, offset: 0, readSafe: readSafe, checkIsGroup: checkIsGroup);
        return new GroupPinFrame(
            new GroupFrame(meta, stream.ReadMemory(checked((int)meta.TotalLength), readSafe: readSafe)),
            pinLocation: loc);
    }














    public static bool TryReadGroup<TStream>(this TStream stream, GameConstants constants, out GroupPinFrame frame, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IBinaryReadStream
    {
        var loc = stream.Position;
        if (!TryGetGroupHeader(stream, constants, out var meta, offset: 0, checkIsGroup: checkIsGroup, readSafe: false))
        {
            frame = default;
            return false;
        }

        frame = new GroupPinFrame(
            new GroupFrame(meta, stream.ReadMemory(checked((int)meta.TotalLength), readSafe: readSafe)),
            pinLocation: loc);
        return true;
    }













    public static MajorRecordHeader GetMajorRecordHeader<TStream>(this TStream stream, GameConstants constants, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        return new MajorRecordHeader(constants, stream.GetMemory(constants.MajorConstants.HeaderLength, offset, readSafe: readSafe));
    }














    public static bool TryGetMajorRecordHeader<TStream>(this TStream stream, GameConstants constants, out MajorRecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.MajorConstants.HeaderLength + offset)
        {
            header = default;
            return false;
        }
        header = GetMajorRecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        return true;
    }















    public static bool TryGetMajorRecordHeader<TStream>(this TStream stream, GameConstants constants, RecordType targetType, out MajorRecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.MajorConstants.HeaderLength + offset)
        {
            header = default;
            return false;
        }
        header = GetMajorRecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        return header.RecordType == targetType;
    }















    public static bool TryGetMajorRecordHeader<TStream>(this TStream stream, GameConstants constants, IReadOnlyCollection<RecordType> targetRecords, out MajorRecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.MajorConstants.HeaderLength + offset)
        {
            header = default;
            return false;
        }
        header = GetMajorRecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        return targetRecords.Contains(header.RecordType);
    }














    public static bool TryReadMajorRecordHeader<TStream>(this TStream stream, GameConstants constants, out MajorRecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.MajorConstants.HeaderLength + offset)
        {
            header = default;
            return false;
        }
        header = ReadMajorRecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        return true;
    }















    public static bool TryReadMajorRecordHeader<TStream>(this TStream stream, GameConstants constants, RecordType targetType,  out MajorRecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.MajorConstants.HeaderLength + offset)
        {
            header = default;
            return false;
        }
        header = ReadMajorRecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        if (header.RecordType != targetType)
        {
            stream.Position -= header.HeaderLength;
            return false;
        }
        return true;
    }















    public static bool TryReadMajorRecordHeader<TStream>(this TStream stream, GameConstants constants, IReadOnlyCollection<RecordType> targetRecords,  out MajorRecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.MajorConstants.HeaderLength + offset)
        {
            header = default;
            return false;
        }
        header = ReadMajorRecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        if (!targetRecords.Contains(header.RecordType))
        {
            stream.Position -= header.HeaderLength;
            return false;
        }
        return true;
    }














    public static MajorRecordFrame GetMajorRecord<TStream>(
        this TStream stream,
        GameConstants constants,
        int offset = 0,
        bool readSafe = true,
        bool automaticallyDecompress = false)
        where TStream : IBinaryReadStream
    {
        var meta = GetMajorRecordHeader(stream, constants, offset, readSafe: readSafe);
        var ret = new MajorRecordFrame(meta, stream.GetMemory(checked((int)meta.TotalLength), offset: offset, readSafe: readSafe));
        if (automaticallyDecompress && ret.IsCompressed)
        {
            return ret.Decompress(out _);
        }

        return ret;
    }













    public static MajorRecordHeader ReadMajorRecordHeader<TStream>(this TStream stream, GameConstants constants, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        return new MajorRecordHeader(constants, stream.ReadMemory(constants.MajorConstants.HeaderLength, offset: offset, readSafe: readSafe));
    }













    public static MajorRecordFrame ReadMajorRecord<TStream>(
        this TStream stream,
        GameConstants constants,
        bool readSafe = true,
        bool automaticallyDecompress = false)
        where TStream : IBinaryReadStream
    {
        var meta = GetMajorRecordHeader(stream, constants, offset: 0, readSafe: readSafe);
        var ret = new MajorRecordFrame(meta, stream.ReadMemory(checked((int)meta.TotalLength), readSafe: readSafe));
        if (automaticallyDecompress && ret.IsCompressed)
        {
            return ret.Decompress(out _);
        }

        return ret;
    }













    public static SubrecordHeader GetSubrecordHeader<TStream>(this TStream stream, GameConstants constants, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        return new SubrecordHeader(constants, stream.GetMemory(constants.SubConstants.HeaderLength, offset, readSafe: readSafe));
    }














    public static bool TryGetSubrecordHeader<TStream>(this TStream stream, GameConstants constants, out SubrecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.SubConstants.HeaderLength + offset)
        {
            header = default;
            return false;
        }
        header = GetSubrecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        return true;
    }















    public static bool TryGetSubrecordHeader<TStream>(this TStream stream, GameConstants constants, RecordType targetType, out SubrecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.SubConstants.HeaderLength)
        {
            header = default;
            return false;
        }
        header = GetSubrecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        return targetType == header.RecordType;
    }















    public static bool TryGetSubrecordHeader<TStream>(this TStream stream, GameConstants constants, IReadOnlyCollection<RecordType> targetRecords, out SubrecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.SubConstants.HeaderLength)
        {
            header = default;
            return false;
        }
        header = GetSubrecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        return targetRecords.Contains(header.RecordType);
    }














    public static bool TryGetSubrecord<TStream>(this TStream stream, GameConstants constants, IReadOnlyCollection<RecordType> targetRecords, out SubrecordFrame frame, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (!TryGetSubrecordHeader(stream, constants, targetRecords, out var meta, readSafe: readSafe, offset: 0))
        {
            frame = default;
            return false;
        }
        frame = SubrecordFrame.FactoryNoTrim(meta, stream.GetMemory(meta.TotalLength, readSafe: readSafe));
        return true;
    }













    public static bool TryGetSubrecord<TStream>(this TStream stream, IReadOnlyCollection<RecordType> targetRecords, out SubrecordFrame frame, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryGetSubrecord(stream, stream.MetaData.Constants, targetRecords, out frame, readSafe: readSafe);
    }













    public static SubrecordFrame GetSubrecord<TStream>(this TStream stream, GameConstants constants, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        var meta = GetSubrecordHeader(stream, constants, offset, readSafe: readSafe);
        return SubrecordFrame.FactoryNoTrim(meta, stream.GetMemory(meta.TotalLength, offset: offset, readSafe: readSafe));
    }














    public static bool TryGetSubrecord<TStream>(this TStream stream, GameConstants constants, out SubrecordFrame frame, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (!TryGetSubrecordHeader(stream, constants, out var meta, readSafe: readSafe, offset: offset))
        {
            frame = default;
            return false;
        }
        frame = SubrecordFrame.FactoryNoTrim(meta, stream.GetMemory(meta.TotalLength, readSafe: readSafe));
        return true;
    }















    public static bool TryGetSubrecord<TStream>(this TStream stream, GameConstants constants, RecordType targetType, out SubrecordFrame frame, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (!TryGetSubrecordHeader(stream, constants, targetType, out var meta, readSafe: readSafe, offset: offset))
        {
            frame = default;
            return false;
        }
        frame = SubrecordFrame.FactoryNoTrim(meta, stream.GetMemory(meta.TotalLength, readSafe: readSafe));
        return true;
    }













    public static SubrecordHeader ReadSubrecordHeader<TStream>(this TStream stream, GameConstants constants, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        return new SubrecordHeader(constants, stream.ReadMemory(constants.SubConstants.HeaderLength, offset: offset, readSafe: readSafe));
    }















    public static SubrecordHeader ReadSubrecordHeader<TStream>(this TStream stream, GameConstants constants, RecordType targetType, int offset = 0, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        var meta = ReadSubrecordHeader(stream, constants, offset: offset, readSafe: readSafe);
        if (meta.RecordType != targetType)
        {
            throw new ArgumentException($"Unexpected header type: {meta.RecordType}");
        }
        return meta;
    }













    public static bool TryReadSubrecordHeader<TStream>(this TStream stream, GameConstants constants, out SubrecordHeader header, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.SubConstants.HeaderLength)
        {
            header = default;
            return false;
        }
        header = ReadSubrecordHeader(stream, constants, readSafe: readSafe);
        return true;
    }














    public static bool TryReadSubrecordHeader<TStream>(this TStream stream, GameConstants constants, RecordType targetType, out SubrecordHeader header, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.SubConstants.HeaderLength)
        {
            header = default;
            return false;
        }
        header = ReadSubrecordHeader(stream, constants, readSafe: readSafe);
        if (header.RecordType != targetType)
        {
            stream.Position -= header.HeaderLength;
            return false;
        }
        return true;
    }














    public static bool TryReadSubrecordHeader<TStream>(this TStream stream, GameConstants constants, IReadOnlyCollection<RecordType> targetRecords, out SubrecordHeader header, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (stream.Remaining < constants.SubConstants.HeaderLength)
        {
            header = default;
            return false;
        }
        header = ReadSubrecordHeader(stream, constants, readSafe: readSafe);
        if (!targetRecords.Contains(header.RecordType))
        {
            stream.Position -= header.HeaderLength;
            return false;
        }
        return true;
    }













    public static SubrecordFrame ReadSubrecord<TStream>(this TStream stream, GameConstants constants, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        var meta = GetSubrecordHeader(stream, constants, readSafe: readSafe, offset: 0);
        return SubrecordFrame.FactoryNoTrim(meta, stream.ReadMemory(meta.TotalLength, readSafe: readSafe));
    }














    public static SubrecordFrame ReadSubrecord<TStream>(this TStream stream, GameConstants constants, RecordType targetType, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        var meta = GetSubrecordHeader(stream, constants, readSafe: readSafe, offset: 0);
        if (meta.RecordType != targetType)
        {
            throw new ArgumentException($"Unexpected header type: {meta.RecordType}");
        }
        return SubrecordFrame.FactoryNoTrim(meta, stream.ReadMemory(meta.TotalLength, readSafe: readSafe));
    }













    public static bool TryReadSubrecord<TStream>(this TStream stream, GameConstants constants, out SubrecordFrame frame, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (!TryGetSubrecordHeader(stream, constants, out var meta, readSafe: readSafe, offset: 0))
        {
            frame = default;
            return false;
        }
        frame = SubrecordFrame.FactoryNoTrim(meta, stream.ReadMemory(meta.TotalLength, readSafe: readSafe));
        return true;
    }














    public static bool TryReadSubrecord<TStream>(this TStream stream, GameConstants constants, RecordType targetType, out SubrecordFrame frame, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (!TryGetSubrecordHeader(stream, constants, targetType, out var meta, readSafe: readSafe, offset: 0))
        {
            frame = default;
            return false;
        }
        frame = SubrecordFrame.FactoryNoTrim(meta, stream.ReadMemory(meta.TotalLength, readSafe: readSafe));
        return true;
    }














    public static bool TryReadSubrecord<TStream>(this TStream stream, GameConstants constants, IReadOnlyCollection<RecordType> targetRecords, out SubrecordFrame frame, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (!TryGetSubrecordHeader(stream, constants, targetRecords, out var meta, readSafe: readSafe, offset: 0))
        {
            frame = default;
            return false;
        }
        frame = SubrecordFrame.FactoryNoTrim(meta, stream.ReadMemory(meta.TotalLength, readSafe: readSafe));
        return true;
    }













    public static VariableHeader GetVariableHeader<TStream>(this TStream stream, GameConstants constants, bool subRecords, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (subRecords)
        {
            return constants.VariableHeader(stream.GetMemory(constants.SubConstants.HeaderLength, readSafe: readSafe), ObjectType.Subrecord);
        }
        RecordType rec = new RecordType(stream.GetInt32());
        if (rec == Constants.Group)
        {
            return constants.VariableHeader(stream.GetMemory(constants.GroupConstants.HeaderLength, readSafe: readSafe), ObjectType.Group);
        }
        else
        {
            return constants.VariableHeader(stream.GetMemory(constants.MajorConstants.HeaderLength, readSafe: readSafe), ObjectType.Record);
        }
    }













    public static VariableHeader ReadVariableHeader<TStream>(this TStream stream, GameConstants constants, bool subRecords, bool readSafe = true)
        where TStream : IBinaryReadStream
    {
        if (subRecords)
        {
            return constants.VariableHeader(stream.GetMemory(constants.SubConstants.HeaderLength, readSafe: readSafe), ObjectType.Subrecord);
        }
        RecordType rec = new RecordType(stream.GetInt32());
        if (rec == Constants.Group)
        {
            return constants.VariableHeader(stream.ReadMemory(constants.GroupConstants.HeaderLength, readSafe: readSafe), ObjectType.Group);
        }
        else
        {
            return constants.VariableHeader(stream.ReadMemory(constants.MajorConstants.HeaderLength, readSafe: readSafe), ObjectType.Record);
        }
    }
    #endregion

    #region Mutagen Stream










    public static ModHeader GetModHeader<TStream>(this TStream stream, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return GetModHeader(stream, stream.MetaData.Constants, readSafe: readSafe);
    }











    public static ModHeader ReadModHeader<TStream>(this TStream stream, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return ReadModHeader(stream, stream.MetaData.Constants, readSafe: readSafe);
    }












    public static bool TryGetModHeader<TStream>(this TStream stream, out ModHeader header, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryGetModHeader(stream, stream.MetaData.Constants, out header, readSafe: readSafe);
    }












    public static bool TryReadModHeader<TStream>(this TStream stream, out ModHeader header, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadModHeader(stream, stream.MetaData.Constants, out header, readSafe: readSafe);
    }











    public static ModHeaderFrame GetModHeaderFrame<TStream>(this TStream stream, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return GetModHeaderFrame(stream, stream.MetaData.Constants, readSafe: readSafe);
    }











    public static ModHeaderFrame ReadModHeaderFrame<TStream>(this TStream stream, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return ReadModHeaderFrame(stream, stream.MetaData.Constants, readSafe: readSafe);
    }












    public static bool TryGetModHeaderFrame<TStream>(this TStream stream, out ModHeaderFrame frame, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryGetModHeaderFrame(stream, stream.MetaData.Constants, out frame, readSafe: readSafe);
    }












    public static bool TryReadModHeaderFrame<TStream>(this TStream stream, out ModHeaderFrame header, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadModHeaderFrame(stream, stream.MetaData.Constants, out header, readSafe: readSafe);
    }














    public static GroupPinHeader GetGroupHeader<TStream>(this TStream stream, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IMutagenReadStream
    {
        return GetGroupHeader(stream, stream.MetaData.Constants, offset: offset, readSafe: readSafe, checkIsGroup: checkIsGroup);
    }














    public static bool TryGetGroupHeader<TStream>(this TStream stream, out GroupPinHeader header, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IMutagenReadStream
    {
        return TryGetGroupHeader(stream, stream.MetaData.Constants, out header, offset: offset, checkIsGroup: checkIsGroup, readSafe: readSafe);
    }














    public static GroupPinFrame GetGroup<TStream>(this TStream stream, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IMutagenReadStream
    {
        return GetGroup(stream, stream.MetaData.Constants, offset: offset, checkIsGroup: checkIsGroup, readSafe: readSafe);
    }














    public static bool TryGetGroup<TStream>(this TStream stream, out GroupPinFrame frame, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IMutagenReadStream
    {
        return TryGetGroup(stream, stream.MetaData.Constants, out frame, offset: offset, checkIsGroup: checkIsGroup, readSafe: readSafe);
    }














    public static GroupPinHeader ReadGroupHeader<TStream>(this TStream stream, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IMutagenReadStream
    {
        return ReadGroupHeader(stream, stream.MetaData.Constants, offset: offset, checkIsGroup: checkIsGroup, readSafe: readSafe);
    }














    public static bool TryReadGroupHeader<TStream>(this TStream stream, out GroupPinHeader header, int offset = 0, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IMutagenReadStream
    {
        return TryReadGroupHeader(stream, stream.MetaData.Constants, out header, offset: offset, checkIsGroup: checkIsGroup, readSafe: readSafe);
    }













    public static GroupPinFrame ReadGroup<TStream>(this TStream stream, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IMutagenReadStream
    {
        return ReadGroup(stream, stream.MetaData.Constants, checkIsGroup: checkIsGroup, readSafe: readSafe);
    }













    public static bool TryReadGroup<TStream>(this TStream stream, out GroupPinFrame frame, bool readSafe = true, bool checkIsGroup = true)
        where TStream : IMutagenReadStream
    {
        return TryReadGroup(stream, stream.MetaData.Constants, out frame, checkIsGroup: checkIsGroup, readSafe: readSafe);
    }












    public static MajorRecordHeader GetMajorRecordHeader<TStream>(this TStream stream, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return GetMajorRecordHeader(stream, stream.MetaData.Constants, offset: offset, readSafe: readSafe);
    }













    public static bool TryGetMajorRecordHeader<TStream>(this TStream stream, out MajorRecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        if (stream.Remaining < stream.MetaData.Constants.MajorConstants.HeaderLength + offset)
        {
            header = default;
            return false;
        }
        header = GetMajorRecordHeader(stream, stream.MetaData.Constants, offset: offset, readSafe: readSafe);
        return true;
    }














    public static bool TryGetMajorRecordHeader<TStream>(this TStream stream, RecordType targetType, out MajorRecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryGetMajorRecordHeader(stream, stream.MetaData.Constants, targetType, out header, offset: offset, readSafe: readSafe);
    }












    public static MajorRecordHeader ReadMajorRecordHeader<TStream>(this TStream stream, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return ReadMajorRecordHeader(stream, stream.MetaData.Constants, offset: offset, readSafe: readSafe);
    }












    public static bool TryReadMajorRecordHeader<TStream>(this TStream stream, out MajorRecordHeader header, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadMajorRecordHeader(stream, stream.MetaData.Constants, out header, readSafe: readSafe);
    }













    public static bool TryReadMajorRecordHeader<TStream>(this TStream stream, RecordType targetType, out MajorRecordHeader header, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadMajorRecordHeader(stream, stream.MetaData.Constants, targetType, out header, readSafe: readSafe);
    }













    public static bool TryReadMajorRecordHeader<TStream>(this TStream stream, IReadOnlyCollection<RecordType> targetRecords, out MajorRecordHeader header, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadMajorRecordHeader(stream, stream.MetaData.Constants, targetRecords, out header, readSafe: readSafe);
    }













    public static MajorRecordFrame GetMajorRecord<TStream>(
        this TStream stream,
        int offset = 0,
        bool readSafe = true,
        bool automaticallyDecompress = false)
        where TStream : IMutagenReadStream
    {
        return GetMajorRecord(stream, stream.MetaData.Constants, offset: offset, readSafe: readSafe, automaticallyDecompress: automaticallyDecompress);
    }












    public static MajorRecordFrame ReadMajorRecord<TStream>(
        this TStream stream,
        bool readSafe = true,
        bool automaticallyDecompress = false)
        where TStream : IMutagenReadStream
    {
        return ReadMajorRecord(stream, stream.MetaData.Constants, readSafe: readSafe, automaticallyDecompress: automaticallyDecompress);
    }












    public static SubrecordHeader GetSubrecordHeader<TStream>(this TStream stream, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return GetSubrecordHeader(stream, stream.MetaData.Constants, offset: offset, readSafe: readSafe);
    }













    public static bool TryGetSubrecordHeader<TStream>(this TStream stream, out SubrecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryGetSubrecordHeader(stream, stream.MetaData.Constants, out header, offset: offset, readSafe: readSafe);
    }














    public static bool TryGetSubrecordHeader<TStream>(this TStream stream, RecordType targetType, out SubrecordHeader header, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryGetSubrecordHeader(stream, stream.MetaData.Constants, targetType, out header, offset: offset, readSafe: readSafe);
    }












    public static SubrecordFrame GetSubrecord<TStream>(this TStream stream, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return GetSubrecord(stream, stream.MetaData.Constants, offset: offset, readSafe: readSafe);
    }












    public static SubrecordFrame GetSubrecord<TStream>(this TStream stream, RecordType targetType, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        var meta = GetSubrecordHeader(stream, stream.MetaData.Constants, readSafe: readSafe, offset: 0);
        if (meta.RecordType != targetType)
        {
            throw new ArgumentException($"Unexpected header type: {meta.RecordType}");
        }

        return SubrecordFrame.FactoryNoTrim(meta, stream.ReadMemory(meta.TotalLength, readSafe: readSafe));
    }













    public static bool TryGetSubrecord<TStream>(this TStream stream, out SubrecordFrame frame, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryGetSubrecord(stream, stream.MetaData.Constants, out frame, offset: offset, readSafe: readSafe);
    }














    public static bool TryGetSubrecord<TStream>(this TStream stream, RecordType targetType, out SubrecordFrame frame, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryGetSubrecord(stream, stream.MetaData.Constants, targetType, out frame, offset: offset, readSafe: readSafe);
    }












    public static SubrecordHeader ReadSubrecordHeader<TStream>(this TStream stream, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return ReadSubrecordHeader(stream, stream.MetaData.Constants, offset: offset, readSafe: readSafe);
    }














    public static SubrecordHeader ReadSubrecordHeader<TStream>(this TStream stream, RecordType targetType, int offset = 0, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return ReadSubrecordHeader(stream, stream.MetaData.Constants, targetType, offset: offset, readSafe: readSafe);
    }












    public static bool TryReadSubrecordHeader<TStream>(this TStream stream, out SubrecordHeader header, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadSubrecordHeader(stream, stream.MetaData.Constants, out header, readSafe: readSafe);
    }













    public static bool TryReadSubrecordHeader<TStream>(this TStream stream, RecordType targetType, out SubrecordHeader header, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadSubrecordHeader(stream, stream.MetaData.Constants, targetType, out header, readSafe: readSafe);
    }












    public static SubrecordFrame ReadSubrecord<TStream>(this TStream stream, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return ReadSubrecord(stream, stream.MetaData.Constants, readSafe: readSafe);
    }













    public static SubrecordFrame ReadSubrecord<TStream>(this TStream stream, RecordType targetType, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return ReadSubrecord(stream, stream.MetaData.Constants, targetType, readSafe: readSafe);
    }












    public static bool TryReadSubrecord<TStream>(this TStream stream, out SubrecordFrame frame, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadSubrecord(stream, stream.MetaData.Constants, out frame, readSafe: readSafe);
    }













    public static bool TryReadSubrecord<TStream>(this TStream stream, RecordType targetType, out SubrecordFrame frame, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadSubrecord(stream, stream.MetaData.Constants, targetType, out frame, readSafe: readSafe);
    }













    public static bool TryReadSubrecord<TStream>(this TStream stream, IReadOnlyCollection<RecordType> targetRecords, out SubrecordFrame frame, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return TryReadSubrecord(stream, stream.MetaData.Constants, targetRecords, out frame, readSafe: readSafe);
    }












    public static VariableHeader GetVariableHeader<TStream>(this TStream stream, bool subRecords, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return GetVariableHeader(stream, stream.MetaData.Constants, subRecords: subRecords, readSafe: readSafe);
    }












    public static VariableHeader ReadVariableHeader<TStream>(this TStream stream, bool subRecords, bool readSafe = true)
        where TStream : IMutagenReadStream
    {
        return ReadVariableHeader(stream, stream.MetaData.Constants, subRecords: subRecords, readSafe: readSafe);
    }
    #endregion
}