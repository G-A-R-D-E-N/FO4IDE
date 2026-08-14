namespace Mutagen.Bethesda.Fallout4;

public interface IScripted : IScriptedGetter
{
    new VirtualMachineAdapter? VirtualMachineAdapter { get; set; }
}

public interface IScriptedGetter
{
    IVirtualMachineAdapterGetter? VirtualMachineAdapter { get; }
}