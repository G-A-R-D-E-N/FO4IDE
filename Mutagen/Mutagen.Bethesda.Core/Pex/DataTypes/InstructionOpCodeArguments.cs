using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Pex;

public static class InstructionOpCodeArguments
{
    [SuppressMessage("ReSharper", "StringLiteralTypo")]
    public static IReadOnlyList<string> Arguments => new[]
    {
        "",
        "SII",
        "SFF",
        "SII",
        "SFF",
        "SII",
        "SFF",
        "SII",
        "SFF",
        "SII",
        "SA",
        "SI",
        "SF",
        "SA",
        "SA",
        "SAA",
        "SAA",
        "SAA",
        "SAA",
        "SAA",
        "L",
        "AL",
        "AL",
        "NSS*",
        "NS*",
        "NNS*",
        "A",
        "SQQ",
        "NSS",
        "NSA",
        "Su",
        "SS",
        "SSI",
        "SIA",
        "SSII",
        "SSII",

        "SSS",
        "S",
        "SS",
        "SSS",
        "SSSSS",
        "SSSSS",
        "SSS",
        "SSS",
        "S",
        "SSS",
        "S"
    };

    public static string GetArguments(InstructionOpcode opcode)
    {
        var index = (byte) opcode;
        if (index >= Arguments.Count)
            throw new ArgumentOutOfRangeException(nameof(opcode), $"Out-of-range: {opcode} with index {index}");
        return Arguments[index];
    }
}