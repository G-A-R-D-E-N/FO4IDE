using System.Collections;
using System.Drawing;
using FO4RecordEditor.Services;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;

namespace FO4RecordEditor.Services.Rendering;







public static class ElementRenderer
{
    private static Func<IFormLinkIdentifier, string> _formatFormLink = MutagenLoader.FormatFormLink;
    private static Func<IConditionGetter, string> _formatCondition = MutagenLoader.FormatCondition;

    public static void Init(
        Func<IFormLinkIdentifier, string> formatFormLink,
        Func<IConditionGetter, string> formatCondition)
    {
        _formatFormLink = formatFormLink;
        _formatCondition = formatCondition;
    }

    public static bool TryRenderLine(object item, out string text)
    {
        switch (item)
        {
            case Color c:
                text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                return true;
            case IConstructibleObjectComponentGetter comp:
                text = $"{_formatFormLink(comp.Component)} x{comp.Count}";
                return true;
            case IConditionGetter cond:
                text = _formatCondition(cond);
                return true;
        }
        text = "";
        return false;
    }

    public static bool TryRenderByteBlob(object collection, out string text)
    {
        text = "";
        if (collection is byte[] ba) { text = $"{ba.Length} bytes"; return true; }
        if (collection is Noggog.MemorySlice<byte> ms) { text = $"{ms.Length} bytes"; return true; }
        if (collection is IEnumerable e and not string)
        {
            int count = 0; bool allBytes = true;
            foreach (var x in e) { if (x is not byte) { allBytes = false; break; } count++; }
            if (allBytes && count > 0) { text = $"{count} bytes"; return true; }
        }
        return false;
    }
}
