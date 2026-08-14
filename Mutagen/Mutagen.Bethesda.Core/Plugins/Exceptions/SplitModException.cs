namespace Mutagen.Bethesda.Plugins.Exceptions;

public class SplitModException : Exception
{
    public SplitModException(string message) : base(message) { }
    public SplitModException(string message, Exception innerException) : base(message, innerException) { }
}