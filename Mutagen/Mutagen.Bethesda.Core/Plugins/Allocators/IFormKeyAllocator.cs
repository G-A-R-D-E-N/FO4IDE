using System.Data;
using System.Diagnostics;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Allocators;

[DebuggerDisplay("FormKeyAllocator {Mod.ModKey}")]
public abstract class BaseFormKeyAllocator : IFormKeyAllocator
{



    public IMod Mod { get; }

    private readonly HashSet<string> _allocatedEditorIDs = new();

    protected BaseFormKeyAllocator(IMod mod)
    {
        Mod = mod;
    }

    public abstract FormKey GetNextFormKey();

    public FormKey GetNextFormKey(string? editorID)
    {
        if (editorID is null) return GetNextFormKey();

        lock (_allocatedEditorIDs)
        {
            if (!_allocatedEditorIDs.Add(editorID))
            {
                throw new ConstraintException($"Attempted to allocate a duplicate unique FormKey for {editorID}");
            }
        }

        return GetNextFormKeyNotNull(editorID);
    }

    protected abstract FormKey GetNextFormKeyNotNull(string editorID);
}




public interface IFormKeyAllocator
{




    FormKey GetNextFormKey();









    FormKey GetNextFormKey(string? editorID);
}