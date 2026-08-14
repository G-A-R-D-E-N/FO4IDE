using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Allocators;







public sealed class SimpleFormKeyAllocator : IFormKeyAllocator
{



    public IMod Mod { get; }




    public SimpleFormKeyAllocator(IMod mod)
    {
        Mod = mod;
    }








    public FormKey GetNextFormKey()
    {
        lock (Mod)
        {
            return new FormKey(
                Mod.ModKey,
                checked(Mod.NextFormID++));
        }
    }

    public FormKey GetNextFormKey(string? editorID)
    {
        return GetNextFormKey();
    }
}