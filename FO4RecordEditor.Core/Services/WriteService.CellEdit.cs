using System.IO;
using Mutagen.Bethesda.Fallout4;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Noggog;

namespace FO4RecordEditor.Services;

public static partial class WriteService
{
    public static string SetPlacedReferenceTransform(
        object? env, string formKeyStr, string patchPlugin,
        float x, float y, float z, float rx, float ry, float rz)
    {
        if (!FormKey.TryFactory(formKeyStr, out var fk)) return "Invalid FormKey.";
        if (string.IsNullOrWhiteSpace(patchPlugin)) return "Choose a patch plugin to save into.";

        if (MutagenLoader.LinkCache is not ILinkCache<IFallout4Mod, IFallout4ModGetter> cache)
            return "No environment loaded. Load Env or Open MO2 first.";

        var (patchName, patchPath) = NormalizePlugin(patchPlugin);
        if (GetMutable(patchName) == null)
        {
            bool existsOnDisk = (patchPath != null && File.Exists(patchPath))
                || MutagenLoader.LooseModPaths.ContainsKey(patchName)
                || FindPluginPath(patchName, env) != null;
            if (existsOnDisk) OpenPlugin(patchPlugin, env);
            else CreatePlugin(patchPlugin);
        }
        var patch = GetMutable(patchName);
        if (patch == null) return $"Could not open or create patch plugin '{patchName}'.";

        try
        {
            IPositionRotation? ovr = null;
            if (cache.TryResolveContext<IPlacedObject, IPlacedObjectGetter>(fk, out var poCtx))
                ovr = poCtx.GetOrAddAsOverride(patch);
            else if (cache.TryResolveContext<IPlacedNpc, IPlacedNpcGetter>(fk, out var pnCtx))
                ovr = pnCtx.GetOrAddAsOverride(patch);
            else if (cache.TryResolveContext<IAPlacedTrap, IAPlacedTrapGetter>(fk, out var ptCtx))
                ovr = ptCtx.GetOrAddAsOverride(patch);

            if (ovr == null)
                return $"Could not find a movable placed reference (PlacedObject/PlacedNpc/PlacedTrap) for {fk}.";

            ovr.Position = new P3Float(x, y, z);
            ovr.Rotation = new P3Float(rx, ry, rz);

            MutagenLoader.InvalidateModIndex(patchName);
            NotifyChanged(patchName);
            return $"Moved {fk} in {patchName}.";
        }
        catch (Exception ex)
        {
            return $"Move failed: {ex.Message}";
        }
    }
}
