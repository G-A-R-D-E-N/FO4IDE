using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Binary.Translations;
using Noggog;
using System.Buffers.Binary;
using Mutagen.Bethesda.Plugins.Binary.Overlay;
using Mutagen.Bethesda.Plugins.Binary.Streams;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Plugins.Records.Internals;
using Mutagen.Bethesda.Strings;

namespace Mutagen.Bethesda;




public static class HeaderExt
{






    public static void AssertLength(this SubrecordFrame frame, int len)
    {
        if (frame.Content.Length != len)
        {
            throw new ArgumentException($"{frame.RecordType} Subrecord frame had unexpected length: {frame.Content.Length} != {len}");
        }
    }

    #region Primitive Extraction






    public static byte AsUInt8(this SubrecordFrame frame)
    {
        frame.AssertLength(1);
        return frame.Content[0];
    }







    public static sbyte AsInt8(this SubrecordFrame frame)
    {
        frame.AssertLength(1);
        return (sbyte)frame.Content[0];
    }







    public static ushort AsUInt16(this SubrecordFrame frame)
    {
        frame.AssertLength(2);
        return BinaryPrimitives.ReadUInt16LittleEndian(frame.Content);
    }







    public static short AsInt16(this SubrecordFrame frame)
    {
        frame.AssertLength(2);
        return BinaryPrimitives.ReadInt16LittleEndian(frame.Content);
    }







    public static uint AsUInt32(this SubrecordFrame frame)
    {
        frame.AssertLength(4);
        return BinaryPrimitives.ReadUInt32LittleEndian(frame.Content);
    }







    public static int AsInt32(this SubrecordFrame frame)
    {
        frame.AssertLength(4);
        return BinaryPrimitives.ReadInt32LittleEndian(frame.Content);
    }







    public static ulong AsUInt64(this SubrecordFrame frame)
    {
        frame.AssertLength(8);
        return BinaryPrimitives.ReadUInt64LittleEndian(frame.Content);
    }







    public static long AsInt64(this SubrecordFrame frame)
    {
        frame.AssertLength(8);
        return BinaryPrimitives.ReadInt64LittleEndian(frame.Content);
    }







    public static float AsFloat(this SubrecordFrame frame)
    {
        frame.AssertLength(4);
        return frame.Content.Float();
    }







    public static double AsDouble(this SubrecordFrame frame)
    {
        frame.AssertLength(8);
        return frame.Content.Double();
    }







    public static string AsString(this SubrecordFrame frame, IMutagenEncoding encoding)
    {
        return BinaryStringUtility.ProcessWholeToZString(frame.Content, encoding);
    }

    #region Pin Forwarding






    public static byte AsUInt8(this SubrecordPinFrame pin) => pin.Frame.AsUInt8();







    public static sbyte AsInt8(this SubrecordPinFrame pin) => pin.Frame.AsInt8();







    public static ushort AsUInt16(this SubrecordPinFrame pin) => pin.Frame.AsUInt16();







    public static short AsInt16(this SubrecordPinFrame pin) => pin.Frame.AsInt16();







    public static uint AsUInt32(this SubrecordPinFrame pin) => pin.Frame.AsUInt32();







    public static int AsInt32(this SubrecordPinFrame pin) => pin.Frame.AsInt32();







    public static ulong AsUInt64(this SubrecordPinFrame pin) => pin.Frame.AsUInt64();







    public static long AsInt64(this SubrecordPinFrame pin) => pin.Frame.AsInt64();







    public static float AsFloat(this SubrecordPinFrame pin) => pin.Frame.AsFloat();







    public static double AsDouble(this SubrecordPinFrame pin) => pin.Frame.AsDouble();







    public static string AsString(this SubrecordPinFrame pin, IMutagenEncoding encoding) => pin.Frame.AsString(encoding);







    public static FormID AsFormID(this SubrecordPinFrame pin)
    {
        return new FormID(pin.AsUInt32());
    }
    #endregion
    #endregion

    #region Find







    public static bool TryFindSubrecordHeader(this MajorRecordFrame majorFrame, RecordType type, out SubrecordPinHeader header)
    {
        var find = RecordSpanExtensions.TryFindSubrecord(majorFrame.Content, majorFrame.Meta, type);
        if (find == null)
        {
            header = default;
            return false;
        }
        header = new SubrecordPinHeader(majorFrame.Meta, majorFrame.Content.Slice(find.Value.Location), find.Value.Location + majorFrame.HeaderLength);
        return true;
    }







    public static SubrecordPinHeader? TryFindSubrecordHeader(this MajorRecordFrame majorFrame, RecordType type)
    {
        if (majorFrame.TryFindSubrecordHeader(type, out var header))
        {
            return header;
        }

        return default;
    }









    public static bool TryFindSubrecordHeader(this MajorRecordFrame majorFrame, RecordType type, int offset, out SubrecordPinHeader header)
    {
        var find = RecordSpanExtensions.TryFindSubrecord(majorFrame.Content.Slice(offset - majorFrame.HeaderLength), majorFrame.Meta, type);
        if (find == null)
        {
            header = default;
            return false;
        }
        header = new SubrecordPinHeader(majorFrame.Meta, majorFrame.Content.Slice(find.Value.Location + offset - majorFrame.HeaderLength), find.Value.Location + offset);
        return true;
    }








    public static SubrecordPinHeader? TryFindSubrecordHeader(this MajorRecordFrame majorFrame, RecordType type, int offset)
    {
        if (TryFindSubrecordHeader(majorFrame, type, offset, out var header))
        {
            return header;
        }

        return default;
    }








    public static bool TryFindSubrecord(this MajorRecordFrame majorFrame, RecordType type, out SubrecordPinFrame pin)
    {
        var find = RecordSpanExtensions.TryFindSubrecord(majorFrame.Content, majorFrame.Meta, type);
        if (find == null)
        {
            pin = default;
            return false;
        }
        pin = find.Value.Shift(majorFrame.HeaderLength);
        return true;
    }







    public static SubrecordPinFrame? TryFindSubrecord(this MajorRecordFrame majorFrame, RecordType type)
    {
        if (TryFindSubrecord(majorFrame, type, out var frame))
        {
            return frame;
        }

        return default;
    }







    public static SubrecordPinFrame? TryFindSubrecord(this MajorRecordFrame majorFrame, params RecordType[] type)
    {
        var find = RecordSpanExtensions.TryFindSubrecord(majorFrame.Content, majorFrame.Meta, type);
        if (find == null)
        {
            return default;
        }
        return find.Value.Frame.Pin(find.Value.Location + majorFrame.HeaderLength);
    }








    public static SubrecordPinFrame? TryFindSubrecordAfter(this MajorRecordFrame majorFrame, SubrecordPinFrame afterSubrecord, params RecordType[] type)
    {
        var spanToSearch = majorFrame.HeaderAndContentData.Slice(afterSubrecord.EndLocation);
        var find = RecordSpanExtensions.TryFindSubrecord(spanToSearch, majorFrame.Meta, type);
        if (find == null)
        {
            return default;
        }
        return find.Value.Frame.Pin(find.Value.Location + afterSubrecord.EndLocation);
    }









    public static bool TryFindSubrecord(this MajorRecordFrame majorFrame, RecordType type, int offset, out SubrecordPinFrame pin)
    {
        var find = RecordSpanExtensions.TryFindSubrecord(majorFrame.Content.Slice(offset - majorFrame.HeaderLength), majorFrame.Meta, type);
        if (find == null)
        {
            pin = default;
            return false;
        }
        pin = new SubrecordPinFrame(majorFrame.Meta, majorFrame.Content.Slice(find.Value.Location + offset - majorFrame.HeaderLength), find.Value.Location + offset);
        return true;
    }








    public static SubrecordPinFrame? TryFindSubrecord(this MajorRecordFrame majorFrame, RecordType type, int offset)
    {
        if (TryFindSubrecord(majorFrame, type, offset, out var frame))
        {
            return frame;
        }

        return default;
    }








    public static SubrecordPinHeader FindSubrecordHeader(this MajorRecordFrame majorFrame, RecordType type)
    {
        if (!TryFindSubrecordHeader(majorFrame, type, out var header))
        {
            throw new ArgumentException($"Could not locate subrecord of type: {type}");
        }
        return header;
    }









    public static SubrecordPinHeader FindSubrecordHeader(this MajorRecordFrame majorFrame, RecordType type, int offset)
    {
        if (!TryFindSubrecordHeader(majorFrame, type, offset, out var header))
        {
            throw new ArgumentException($"Could not locate subrecord of type: {type}");
        }
        return header;
    }








    public static SubrecordPinFrame FindSubrecord(this MajorRecordFrame majorFrame, RecordType type)
    {
        if (!TryFindSubrecord(majorFrame, type, out var pin))
        {
            throw new ArgumentException($"Could not locate subrecord of type: {type}");
        }
        return pin;
    }









    public static SubrecordPinFrame FindSubrecord(this MajorRecordFrame majorFrame, RecordType type, int offset)
    {
        if (!TryFindSubrecord(majorFrame, type, offset, out var pin))
        {
            throw new ArgumentException($"Could not locate subrecord of type: {type}");
        }
        return pin;
    }
    #endregion

    #region Iterate










    public static IEnumerable<SubrecordPinFrame> FindEnumerateSubrecords(this MajorRecordFrame majorFrame, RecordType type, bool onlyFirstSet = false)
    {
        bool encountered = false;
        foreach (var subrecord in majorFrame)
        {
            if (subrecord.RecordType == type)
            {
                encountered = true;
                yield return subrecord;
            }
            else if (onlyFirstSet && encountered)
            {
                yield break;
            }
        }
    }












    public static IEnumerable<SubrecordPinFrame> FindEnumerateSubrecordsAfter(this MajorRecordFrame majorFrame, RecordType type, SubrecordPinFrame afterSubrecord, bool onlyFirstSet = false)
    {
        bool encountered = false;
        foreach (var subrecord in majorFrame)
        {
            if (subrecord.Location <= afterSubrecord.Location) continue;
            if (subrecord.RecordType == type)
            {
                encountered = true;
                yield return subrecord;
            }
            else if (onlyFirstSet && encountered)
            {
                yield break;
            }
        }
    }







    public static IEnumerable<SubrecordPinFrame> FindEnumerateSubrecords(this MajorRecordFrame majorFrame, IReadOnlyCollection<RecordType> recordTypes)
    {
        foreach (var subrecord in majorFrame)
        {
            if (recordTypes.Contains(subrecord.RecordType))
            {
                yield return subrecord;
            }
        }
    }







    public static IEnumerable<SubrecordPinFrame> EnumerateSubrecords(this MajorRecordFrame majorFrame)
    {
        return RecordSpanExtensions.EnumerateSubrecords(majorFrame.HeaderAndContentData, majorFrame.Meta, majorFrame.HeaderLength);
    }






    public static IEnumerable<SubrecordPinFrame> EnumerateSubrecords(this ModHeaderFrame modHeader)
    {
        return RecordSpanExtensions.EnumerateSubrecords(modHeader.HeaderAndContentData, modHeader.Meta, modHeader.HeaderLength);
    }

    public static IEnumerable<SubrecordPinFrame> MasterSubrecords(this ModHeaderFrame modHeader)
    {
        foreach (var pin in EnumerateSubrecords(modHeader))
        {
            if (pin.RecordType == RecordTypes.MAST)
            {
                yield return pin;
            }
        }
    }

    public static IEnumerable<IMasterReferenceGetter> Masters(this ModHeaderFrame modHeader, ModKey modKey)
    {
        var package = new BinaryOverlayFactoryPackage(
            new ParsingMeta(modHeader.Meta, modKey, masterReferences: null!));
        return modHeader
            .MasterSubrecords()
            .Select(mastPin =>
            {
                return MasterReferenceBinaryOverlay.MasterReferenceFactory(
                        mastPin.HeaderAndContentData,
                        package)

                    .DeepCopy();
            });
    }

    public static MasterReferenceCollection ToMasterReferenceCollection(this ModHeaderFrame modHeader, ModKey modKey)
    {
        return MasterReferenceCollection.FromModHeader(modKey, modHeader);
    }






    public static IEnumerable<VariablePinHeader> EnumerateRecords(GroupFrame group)
    {
        int loc = group.HeaderLength;
        while (loc < group.HeaderAndContentData.Length)
        {
            var subHeader = group.Meta.VariableHeader(group.HeaderAndContentData, loc);
            yield return subHeader;
            loc = checked((int)(loc + subHeader.TotalLength));
        }
    }

    public static IEnumerable<MajorRecordPinFrame> EnumerateMajorRecords(this GroupFrame group)
    {
        foreach (var varRec in group)
        {
            if (varRec.IsGroup) continue;
            yield return new MajorRecordPinFrame(group.Meta, group.HeaderAndContentData.Slice(varRec.Location), varRec.Location);
        }
    }

    public static IEnumerable<GroupPinFrame> EnumerateSubGroups(this GroupFrame group)
    {
        foreach (var varRec in group)
        {
            if (!varRec.IsGroup) continue;
            yield return new GroupPinFrame(group.Meta, group.HeaderAndContentData.Slice(varRec.Location), varRec.Location);
        }
    }

    public static IEnumerable<MajorRecordPinFrame> EnumerateMajorRecords(this GroupPinFrame group) => group.Frame.EnumerateMajorRecords();

    public static IEnumerable<GroupPinFrame> EnumerateSubGroups(this GroupPinFrame group) => group.Frame.EnumerateSubGroups();
    #endregion
}