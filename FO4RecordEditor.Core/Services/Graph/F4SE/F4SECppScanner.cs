using System;
using System.Collections.Generic;
using System.Text;

namespace FO4RecordEditor.Services.Graph.F4SE;










public static class F4SECppScanner
{














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



                case ';':
                case '{':
                case '}':
                    return -1;
            }
        }
        return -1;
    }


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
