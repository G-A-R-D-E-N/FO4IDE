namespace Mutagen.Bethesda.Plugins.Records;

public interface IModFlagsGetter : IModMasterStyledGetter
{



    bool CanUseLocalization { get; }




    bool UsingLocalization { get; }




    bool CanBeSmallMaster { get; }




    bool IsSmallMaster { get; }




    bool CanBeMediumMaster { get; }




    bool IsMediumMaster { get; }




    bool IsMaster { get; }




    bool ListsOverriddenForms { get; }
}

public interface IModMasterStyledGetter : IModKeyed
{
    MasterStyle MasterStyle { get; }
}

public record ModFlags : IModFlagsGetter
{
    public ModKey ModKey { get; init; }
    public bool CanUseLocalization { get; init; }
    public bool UsingLocalization { get; init; }
    public bool CanBeSmallMaster { get; init; }
    public bool IsSmallMaster { get; init; }
    public bool CanBeMediumMaster { get; init; }
    public bool IsMediumMaster { get; init; }
    public bool IsMaster { get; init; }
    public bool ListsOverriddenForms { get; init; }

    public ModFlags(ModKey modKey)
    {
        ModKey = modKey;
    }

    public ModFlags(IModFlagsGetter flags)
    {
        ModKey = flags.ModKey;
        CanUseLocalization = flags.CanUseLocalization;
        UsingLocalization = flags.UsingLocalization;
        CanBeSmallMaster = flags.CanBeSmallMaster;
        IsSmallMaster = flags.IsSmallMaster;
        CanBeMediumMaster = flags.CanBeMediumMaster;
        IsMediumMaster = flags.IsMediumMaster;
        IsMaster = flags.IsMaster;
        ListsOverriddenForms = flags.ListsOverriddenForms;
    }

    public MasterStyle MasterStyle => this.GetMasterStyle();
}