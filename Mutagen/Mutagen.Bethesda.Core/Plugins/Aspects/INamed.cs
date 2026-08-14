using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda
{
    namespace Plugins.Aspects
    {



        public interface INamed : INamedGetter, INamedRequired
        {



            new String? Name { get; set; }
        }




        public interface INamedGetter : INamedRequiredGetter
        {



            new String? Name { get; }
        }
    }

    public static class INamedExt
    {






        public static bool NamedFieldsContain<TMajor>(this TMajor named, string str)
            where TMajor : INamedGetter, IMajorRecordGetter
        {
            if (named.EditorID?.Contains(str) ?? false) return true;
            if (named.Name?.Contains(str) ?? false) return true;
            return false;
        }








        public static bool NamedFieldsContain<TMajor>(this TMajor named, string str, StringComparison stringComparison)
            where TMajor : INamedGetter, IMajorRecordGetter
        {
            if (named.EditorID?.Contains(str, stringComparison) ?? false) return true;
            if (named.Name?.Contains(str, stringComparison) ?? false) return true;
            return false;
        }
    }
}
