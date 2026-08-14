using System.Diagnostics;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins.Order;


[DebuggerDisplay("{ToString()}")]
public sealed record ModListing : IModListingGetter
{

    public ModKey ModKey { get; init; }


    public bool Enabled { get; init; }


    public bool ModExists { get; init; } = true;


    [Obsolete("Use ModExists instead")]
    public bool ExistsOnDisk => ModExists;


    public bool Ghosted => !string.IsNullOrWhiteSpace(GhostSuffix);


    public string FileName => OrderUtility.GetListingFilename(ModKey, GhostSuffix);


    public string GhostSuffix { get; init; } = string.Empty;

    public ModListing()
    {
    }

    public ModListing(ModKey modKey, bool enabled, bool modExists, string ghostSuffix = "")
    {
        ModKey = modKey;
        ModExists = modExists;
        Enabled = enabled;
        GhostSuffix = ghostSuffix;
    }

    public override string ToString()
    {
        return IModListingGetter.ToString(this);
    }

    public static Comparer<ModListing> GetComparer(Comparer<ModKey> comparer) =>
        Comparer<ModListing>.Create((x, y) => comparer.Compare(x.ModKey, y.ModKey));

    public static Comparer<TListing> GetComparer<TListing>(Comparer<ModKey> comparer)
        where TListing : ILoadOrderListingGetter
    {
        return Comparer<TListing>.Create((x, y) => comparer.Compare(x.ModKey, y.ModKey));
    }
}


[DebuggerDisplay("{ToString()}")]
public sealed record ModListing<TMod> : IModListing<TMod>
    where TMod : class, IModKeyed
{

    public ModKey ModKey { get; init; }


    public bool Enabled { get; init; }


    public bool ModExists => Mod != null;


    [Obsolete("Use ModExists instead")]
    public bool ExistsOnDisk => ModExists;


    public bool Ghosted => !string.IsNullOrWhiteSpace(GhostSuffix);


    public string GhostSuffix { get; init; } = string.Empty;


    public string FileName => OrderUtility.GetListingFilename(ModKey, GhostSuffix);


    public TMod? Mod { get; set; }

    public ModListing(ModKey key, TMod? mod, bool enabled, string ghostSuffix = "")
    {
        ModKey = key;
        Mod = mod;
        Enabled = enabled;
        GhostSuffix = ghostSuffix;
    }




    public ModListing(TMod mod, bool enabled = true, string ghostSuffix = "")
    {
        ModKey = mod.ModKey;
        Mod = mod;
        Enabled = enabled;
        GhostSuffix = ghostSuffix;
    }












    public static ModListing<TMod> CreateUnloaded(ModKey key, bool enabled, string ghostSuffix = "")
    {
        return new ModListing<TMod>(key, default, enabled: enabled, ghostSuffix: ghostSuffix);
    }


    public override string ToString()
    {
        return IModListingGetter<TMod>.ToString(this);
    }

    public void Dispose()
    {
        if (Mod is IDisposable disp)
        {
            disp.Dispose();
        }
    }
}







public interface IModListingGetter<out TMod> : IModListingGetter, IDisposable
    where TMod : class, IModKeyed
{



    TMod? Mod { get; }
}


public interface IModListing<TMod> : IModListingGetter<TMod>
    where TMod : class, IModKeyed
{



    new TMod? Mod { get; set; }
}





public interface IModListingGetter : ILoadOrderListingGetter
{
    public bool ModExists { get; }

    [Obsolete("Use ModExists instead")]
    public bool ExistsOnDisk => ModExists;

    public static string ToString(IModListingGetter getter)
    {
        return $"[{(getter.Enabled ? "X" : "_")}] {getter.ModKey}{(getter.ModExists ? null : " (missing)")}{(getter.Ghosted ? " (ghosted)" : null)}";
    }
}


public interface IModListing : IModListingGetter
{



    new bool Enabled { get; set; }






    new string GhostSuffix { get; set; }
}