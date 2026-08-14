using System;
using System.Collections.Generic;
using System.IO;

namespace FO4RecordEditor.Services.Papyrus;

/// <summary>
/// Recursive file enumeration that survives the directories a real machine actually has.
/// </summary>
/// <remarks>
/// <see cref="Directory.EnumerateFiles(string, string, EnumerationOptions)"/> with
/// <c>RecurseSubdirectories</c> aborts the whole walk on the first directory it cannot open, and
/// <c>IgnoreInaccessible</c> does not cover all of them: a drive that has run a game under Proton
/// carries compatdata prefixes whose <c>dosdevices</c> entries are symlinks into <c>/proc</c>, and
/// opening one whose process has exited throws <see cref="IOException"/>. An import root pointed at
/// a game install or a modlist drive hits this, and losing the entire index to one such directory is
/// not an acceptable failure.
/// <para>
/// Two deliberate choices beyond the error handling:
/// </para>
/// <list type="bullet">
/// <item><b>Symlinked directories are not followed.</b> A Proton prefix's <c>dosdevices/z:</c> points
/// at the filesystem root, so following links turns a per-folder walk into a whole-machine one.</item>
/// <item><b><see cref="FileAttributes"/> are not skipped.</b> The enumeration default omits Hidden
/// and System, which silently drops everything under a dotted directory -- on this machine that was
/// nearly half of what a whole-drive sweep should have found.</item>
/// </list>
/// </remarks>
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
                // Unreadable subtree. The rest of the walk still counts.
            }
        }
    }
}
