using System.Runtime.CompilerServices;

namespace Mutagen.Bethesda.WPF.Reflection.Attributes;

[AttributeUsage(
    AttributeTargets.Field | AttributeTargets.Property,
    AllowMultiple = false)]
public class MaintainOrder : Attribute
{
    public int Order { get; }

    public MaintainOrder([CallerLineNumber] int order = 0)
    {
        Order = order;
    }
}