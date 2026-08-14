using Mutagen.Bethesda.Plugins.Binary.Parameters;

namespace Mutagen.Bethesda.Plugins.Records
{



    [Obsolete("Use ModFactory instead")]
    public static class ModInstantiator
    {



        [Obsolete("Use ModFactory.ImportGetter instead")]
        public static IModDisposeGetter Importer(ModPath path, GameRelease release, BinaryReadParameters? param = null)
        {
            return ModFactory.ImportGetter(path, release, param);
        }




        [Obsolete("Use ModFactory.ImportGetter instead")]
        public static IModDisposeGetter ImportGetter(ModPath path, GameRelease release, BinaryReadParameters? param = null)
        {
            return ModFactory.ImportGetter(path, release, param);
        }




        [Obsolete("Use ModFactory.ImportSetter instead")]
        public static IMod ImportSetter(ModPath path, GameRelease release, BinaryReadParameters? param = null)
        {
            return ModFactory.ImportSetter(path, release, param);
        }




        [Obsolete("Use ModFactory.Activator instead")]
        public static IMod Activator(ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false)
        {
            return ModFactory.Activator(modKey, release, headerVersion, forceUseLowerFormIDRanges);
        }
    }







    [Obsolete("Use ModFactory<TMod> instead")]
    public static class ModInstantiator<TMod>
        where TMod : IModGetter
    {
        public delegate TMod ActivatorDelegate(ModKey modKey, GameRelease release, float? headerVersion = null, bool? forceUseLowerFormIDRanges = false);
        public delegate TMod ImporterDelegate(ModPath modKey, GameRelease release, BinaryReadParameters? param = null);




        [Obsolete("Use ModFactory<TMod>.Activator instead")]
        public static ActivatorDelegate Activator => (modKey, release, headerVersion, forceUseLowerFormIDRanges)
            => ModFactory<TMod>.Activator(modKey, release, headerVersion, forceUseLowerFormIDRanges);




        [Obsolete("Use ModFactory<TMod>.Importer instead")]
        public static ImporterDelegate Importer => (modKey, release, param)
            => ModFactory<TMod>.Importer(modKey, release, param);
    }
}
