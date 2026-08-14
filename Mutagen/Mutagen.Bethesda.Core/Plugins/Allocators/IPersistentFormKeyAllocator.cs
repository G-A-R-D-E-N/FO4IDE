using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Allocators;

public abstract class BasePersistentFormKeyAllocator : BaseFormKeyAllocator, IPersistentFormKeyAllocator
{
    protected string _saveLocation;

    public bool CommitOnDispose = true;

    private bool _disposed = false;

    protected BasePersistentFormKeyAllocator(IMod mod, string saveLocation) : base(mod)
    {
        _saveLocation = Path.GetFullPath(saveLocation);
    }




    public abstract void Commit();




    public abstract void Rollback();

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            if (CommitOnDispose) Commit();
        }
        _disposed = true;
    }

    public void Dispose() => Dispose(true);
}




public interface IPersistentFormKeyAllocator : IFormKeyAllocator, IDisposable
{
    public void Commit();

    public void Rollback();
}