namespace Mutagen.Bethesda.Plugins.Aspects;

public sealed class CustomAspectInterface : Attribute
{
    public Type[] KnownTypes;

    public CustomAspectInterface(params Type[] knownTypes)
    {
        KnownTypes = knownTypes;
    }
}