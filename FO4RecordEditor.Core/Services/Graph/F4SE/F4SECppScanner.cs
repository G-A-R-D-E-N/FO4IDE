using System;
using System.Collections.Generic;
using System.Text;

namespace FO4RecordEditor.Services.Graph.F4SE;

/// <summary>
/// The small amount of C++ text handling the registration extractor needs.
/// </summary>
/// <remarks>
/// Deliberately not a C++ parser. Recovering registrations needs three things a parser would be
/// enormous overkill for: knowing which text is a comment, knowing where a template argument list
/// ends, and splitting that list at the commas that separate arguments rather than the ones inside
/// a nested template.
/// </remarks>
public static class F4SECppScanner
{
    /// <summary>
    /// Blanks out comments while leaving every other byte, including string literals, in place.
    /// </summary>
    /// <remarks>
    /// String literals are kept because the registration's function and class names live in them.
    /// Comment text is replaced with spaces rather than deleted so that every offset in the result
    /// still matches the input, which is what lets a recovered binding carry a true line number.
    /// <para>
    /// The F4SE trees in this workspace contain no commented-out registrations in either version,
    /// so this is not fixing an observed miscount. It is here because the extractor also runs over
    /// plugin sources this project generates and over third party sources it has never seen, where
    /// a commented-out registration would otherwise be recovered as a real one.
    /// </para>
    /// </remarks>
    public static string BlankComments(string source)
    {
        if (string.IsNullOrEmpty(source)) return source ?? "";

        var result = new StringBuilder(source.Length);
        int i = 0;

        while (i < source.Length)
        {
            char c = source[i];

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n') { result.Append(' '); i++; }
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                result.Append("  ");
                i += 2;
                while (i < source.Length && !(source[i] == '*' && i + 1 < source.Length && source[i + 1] == '/'))
                {
                    // Newlines survive so line numbering downstream stays correct.
                    result.Append(source[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                if (i < source.Length) { result.Append("  "); i += 2; }
                continue;
            }

            if (c == '"' || c == '\'')
            {
                char quote = c;
                result.Append(c);
                i++;
                while (i < source.Length)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        result.Append(source[i]).Append(source[i + 1]);
                        i += 2;
                        continue;
                    }
                    result.Append(source[i]);
                    if (source[i] == quote) { i++; break; }
                    i++;
                }
                continue;
            }

            result.Append(c);
            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// The index of the <c>&gt;</c> closing the template argument list that opens at
    /// <paramref name="openIndex"/>, or -1 when it is never closed.
    /// </summary>
    /// <remarks>
    /// A regex cannot do this: <c>VMArray&lt;BGSMod::Attachment::Mod*&gt;</c> nests, so a
    /// non-greedy match stops at the wrong angle bracket. Only depth counting is correct.
    /// </remarks>
    public static int FindMatchingAngle(string text, int openIndex)
    {
        if (openIndex < 0 || openIndex >= text.Length || text[openIndex] != '<') return -1;

        int depth = 0;
        for (int i = openIndex; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    if (depth == 0) return i;
                    break;

                // A registration's template list never contains these, so meeting one means the
                // '<' was a comparison rather than a template and the caller should move on.
                case ';':
                case '{':
                case '}':
                    return -1;
            }
        }
        return -1;
    }

    /// <summary>Splits a template argument list at its top-level commas.</summary>
    public static IReadOnlyList<string> SplitTemplateArguments(string arguments)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments)) return parts;

        int depth = 0, start = 0;
        for (int i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case ',' when depth == 0:
                    parts.Add(arguments[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        var last = arguments[start..].Trim();
        if (last.Length > 0) parts.Add(last);
        return parts;
    }

    /// <summary>The 1-based line number of <paramref name="offset"/>.</summary>
    public static int LineAt(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n') line++;
        }
        return line;
    }
}
