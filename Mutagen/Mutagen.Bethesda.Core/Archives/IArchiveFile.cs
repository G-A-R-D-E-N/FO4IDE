using Noggog;

namespace Mutagen.Bethesda.Archives;

public interface IArchiveFile
{



    string Path { get; }




    uint Size { get; }





    byte[] GetBytes();





    ReadOnlySpan<byte> GetSpan();





    ReadOnlyMemorySlice<byte> GetMemorySlice();





    Stream AsStream();
}