using System.Text;

namespace Mutagen.Bethesda.Pex;

public static class PexMixIn
{






    public static void WritePexFile(this PexFile pexFile, string outputPath, GameCategory gameCategory)
    {
        var dirName = Path.GetDirectoryName(outputPath);
        Directory.CreateDirectory(dirName ?? string.Empty);

        if (File.Exists(outputPath))
            File.Delete(outputPath);

        using var fs = File.Open(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        using var bw = new PexWriter(fs, Encoding.UTF8, gameCategory.IsBigEndian());

        pexFile.Write(bw);
    }
}