using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins.Exceptions;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Analysis;




public static class MultiModFileAnalysis
{









    public static bool IsMultiModFile(ModPath modPath, IFileSystem? fileSystem = null)
    {
        fileSystem = fileSystem.GetOrDefault();
        var folder = new DirectoryPath(Path.GetDirectoryName(modPath.Path) ?? ".");
        var modKey = modPath.ModKey;
        var splitFiles = DetectSplitFiles(folder, modKey, fileSystem);

        if (splitFiles.Count == 0)
        {
            return false;
        }

        if (splitFiles.Count == 1)
        {
            throw new SplitModException(
                $"Found only one split file for {modKey}. Expected at least 2 split files (base and _2).");
        }

        return true;
    }











    public static List<FilePath> GetSplitModFiles(ModPath modPath, IFileSystem? fileSystem = null)
    {
        fileSystem = fileSystem.GetOrDefault();
        var folder = new DirectoryPath(Path.GetDirectoryName(modPath.Path) ?? ".");
        var modKey = modPath.ModKey;
        var splitFiles = DetectSplitFiles(folder, modKey, fileSystem);

        if (splitFiles.Count == 0)
        {
            return splitFiles;
        }

        if (splitFiles.Count == 1)
        {
            throw new SplitModException(
                $"Found only one split file for {modKey}. Expected at least 2 split files (base and _2).");
        }

        return splitFiles;
    }





    public static bool IsSplitFileName(string candidateNameWithoutExt, string baseNameWithoutExt)
    {
        return IsSplitFileName(candidateNameWithoutExt, baseNameWithoutExt, out _);
    }





    public static bool IsSplitFileName(string candidateNameWithoutExt, string baseNameWithoutExt, out int splitIndex)
    {
        splitIndex = 0;
        var prefix = baseNameWithoutExt + "_";
        if (!candidateNameWithoutExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var suffix = candidateNameWithoutExt.Substring(prefix.Length);
        return int.TryParse(suffix, out splitIndex);
    }





    public static bool IsSplitModSibling(ModKey candidate, ModKey baseModKey)
    {
        if (candidate.Type != baseModKey.Type) return false;
        return IsSplitFileName(candidate.Name, baseModKey.Name);
    }





    internal static List<FilePath> DetectSplitFiles(DirectoryPath folder, ModKey modKey, IFileSystem fileSystem)
    {
        var splitFiles = new List<FilePath>();
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(modKey.FileName);
        var extension = Path.GetExtension(modKey.FileName);


        var secondFile = Path.Combine(folder.Path, $"{fileNameWithoutExtension}_2{extension}");
        if (!fileSystem.File.Exists(secondFile))
        {
            return splitFiles;
        }


        var baseFile = Path.Combine(folder.Path, modKey.FileName);
        if (!fileSystem.File.Exists(baseFile))
        {
            return splitFiles;
        }


        splitFiles.Add(baseFile);

        int index = 2;
        while (true)
        {
            var splitFileName = $"{fileNameWithoutExtension}_{index}{extension}";
            var splitPath = Path.Combine(folder.Path, splitFileName);

            if (!fileSystem.File.Exists(splitPath)) break;

            splitFiles.Add(splitPath);
            index++;
        }

        return splitFiles;
    }
}
