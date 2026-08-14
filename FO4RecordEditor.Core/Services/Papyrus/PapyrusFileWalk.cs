using System;
using System.Collections.Generic;
using System.IO;

namespace FO4RecordEditor.Services.Papyrus;

public static class PapyrusFileWalk
{
    public static IEnumerable<string> EnumerateFiles(string root, string pattern)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) yield break;

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.None,
        };

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            List<string> files;
            try
            {
                files = new List<string>(Directory.EnumerateFiles(dir, pattern, options));
            }
            catch (Exception)
            {
                continue;
            }
            foreach (var file in files) yield return file;

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir, "*", options))
                {
                    if (new DirectoryInfo(sub).LinkTarget is null) pending.Push(sub);
                }
            }
            catch (Exception)
            {

            }
        }
    }
}
