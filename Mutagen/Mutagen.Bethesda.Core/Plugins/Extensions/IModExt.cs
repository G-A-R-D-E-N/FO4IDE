using System.IO.Abstractions;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Binary.Parameters;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;
using Noggog.IO;

namespace Mutagen.Bethesda;

public static class IModExt
{













    public static IGroupGetter<TMajor> GetTopLevelGroup<TMajor>(this IModGetter mod)
        where TMajor : IMajorRecordGetter
    {
        return mod.TryGetTopLevelGroup<TMajor>() ?? throw new ArgumentException($"Unknown major record type: {typeof(TMajor)}");
    }













    public static IGroupGetter GetTopLevelGroup(this IModGetter mod, Type type)
    {
        return mod.TryGetTopLevelGroup(type) ?? throw new ArgumentException($"Unknown major record type: {type}");
    }












    public static IGroup<TMajor> GetTopLevelGroup<TMajor>(this IMod mod)
        where TMajor : IMajorRecord
    {
        return mod.TryGetTopLevelGroup<TMajor>() ?? throw new ArgumentException($"Unknown major record type: {typeof(TMajor)}");
    }













    public static IGroup GetTopLevelGroup(this IMod mod, Type type)
    {
        return mod.TryGetTopLevelGroup(type) ?? throw new ArgumentException($"Unknown major record type: {type}");
    }

    public static MasterStyle GetMasterStyle(this IModFlagsGetter mod)
    {
        bool small = mod.CanBeSmallMaster && mod.IsSmallMaster;
        bool medium = mod.CanBeMediumMaster && mod.IsMediumMaster;

        if (small && medium)
        {
            throw new ModHeaderMalformedException(mod.ModKey, "Mod was both a light and medium master");
        }

        if (small) return MasterStyle.Small;
        if (medium) return MasterStyle.Medium;
        return MasterStyle.Full;
    }








    public static IMod Duplicate(this IModGetter mod, ModKey newModKey)
    {
        if (mod.ModKey.Type != newModKey.Type) throw new ArgumentException("ModKey types must match");

        var oldModKey = mod.ModKey;
        var fileSystem = new FileSystem();
        using var tmp = TempFolder.Factory();
        var fileSystemRoot = tmp.Dir;
        var oldModPath = new ModPath(oldModKey, fileSystem.Path.Combine(fileSystemRoot, oldModKey.FileName.String));


        mod.WriteToBinary(oldModPath, BinaryWriteParameters.Default with
        {
            FileSystem = fileSystem
        });


        var newModPath = new ModPath(newModKey, fileSystem.Path.Combine(fileSystemRoot, newModKey.FileName.String));
        fileSystem.File.Move(oldModPath, newModPath);


        var stringsDir = fileSystem.Path.Combine(fileSystemRoot, "Strings");
        if (fileSystem.Directory.Exists(stringsDir))
        {
            foreach (var file in fileSystem.Directory.EnumerateFiles(stringsDir))
            {
                var fileName = fileSystem.Path.GetFileName(file);
                if (fileName.StartsWith(oldModKey.Name))
                {
                    fileSystem.File.Move(file, fileSystem.Path.Combine(stringsDir, newModKey.Name + fileName[oldModKey.Name.Length..]));
                }
            }
        }


        var duplicateInto = ModFactory.ImportSetter(newModPath, mod.GameRelease, BinaryReadParameters.Default with
        {
            FileSystem = fileSystem
        });
        return duplicateInto;
    }


















    public static FormID GetFormID(
        this IModGetter mod,
        FormKey formKey,
        IReadOnlyCache<IModMasterStyledGetter, ModKey>? masterFlagLookup = null)
    {
        var masters = new MasterReferenceCollection(mod.ModKey, mod.MasterReferences);
        var package = SeparatedMasterPackage.Factory(
            mod.GameRelease,
            mod.ModKey,
            mod.GetMasterStyle(),
            masters,
            masterFlagLookup);

        return package.GetFormID(formKey);
    }
}