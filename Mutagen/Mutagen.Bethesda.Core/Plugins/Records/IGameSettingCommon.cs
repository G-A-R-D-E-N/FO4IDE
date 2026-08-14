using Mutagen.Bethesda.Plugins.Binary.Headers;
using Mutagen.Bethesda.Plugins.Meta;
using Noggog;
using Mutagen.Bethesda.Plugins.Records.Internals;
using Mutagen.Bethesda.Strings.DI;

namespace Mutagen.Bethesda.Plugins.Records;

public enum GameSettingType
{
    Float,
    Int,
    String,
    Bool,
    UInt
}

public interface IGameSettingCommon : IMajorRecord
{

    GameSettingType SettingType { get; }
}

public interface IGameSettingNumeric : IGameSettingCommon
{

    float? RawData { get; set; }
}

public static class GameSettingUtility
{

    public const char IntChar = 'i';

    public const char FloatChar = 'f';

    public const char StringChar = 's';

    public const char BoolChar = 'b';

    public const char UIntChar = 'u';

    public static bool TryGetGameSettingType(char c, out GameSettingType type)
    {
        switch (c)
        {
            case IntChar:
                type = GameSettingType.Int;
                return true;
            case StringChar:
                type = GameSettingType.String;
                return true;
            case FloatChar:
                type = GameSettingType.Float;
                return true;
            case BoolChar:
                type = GameSettingType.Bool;
                return true;
            case UIntChar:
                type = GameSettingType.UInt;
                return true;
            default:
                type = default;
                return false;
        }
    }

    public static char GetChar(this GameSettingType type)
    {
        switch (type)
        {
            case GameSettingType.Float:
                return FloatChar;
            case GameSettingType.Int:
                return IntChar;
            case GameSettingType.String:
                return StringChar;
            case GameSettingType.Bool:
                return BoolChar;
            case GameSettingType.UInt:
                return UIntChar;
            default:
                throw new NotImplementedException();
        }
    }

    public static string CorrectEDID(string input, GameSettingType type)
    {
        char triggerChar = type.GetChar();
        input = input.Trim();
        if (input.Length == 0)
        {
            return string.Empty + triggerChar;
        }
        else if (!triggerChar.Equals(input[0]))
        {
            return triggerChar + input;
        }
        return input;
    }

    public static GetResponse<GameSettingType> GetGameSettingType(ReadOnlyMemorySlice<byte> span, GameConstants meta)
    {
        var majorMeta = meta.MajorRecord(span);
        var edidFrame = majorMeta.FindSubrecord(RecordTypes.EDID);
        var edid = edidFrame.AsString(MutagenEncoding._1252);
        if (edid.Length == 0)
        {
            return GetResponse<GameSettingType>.Fail("No EDID parsed.");
        }
        if (!TryGetGameSettingType(edid[0], out var settingType))
        {
            return GetResponse<GameSettingType>.Fail($"Unknown game setting type: {edid[0]}");
        }
        return GetResponse<GameSettingType>.Succeed(settingType);
    }
}