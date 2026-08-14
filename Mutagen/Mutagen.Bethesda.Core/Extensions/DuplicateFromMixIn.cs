using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Internals;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda;

public static class DuplicateFromMixIn
{


















    public static void DuplicateFromOnlyReferenced<TMod, TModGetter>(
        this TMod modToDuplicateInto,
        ILinkCache<TMod, TModGetter> linkCache,
        ModKey modKeyToDuplicateFrom,
        params Type[] typesToInspect)
        where TModGetter : class, IModGetter
        where TMod : class, TModGetter, IMod
    {
        DuplicateFromOnlyReferenced(
            modToDuplicateInto,
            linkCache,
            modKeyToDuplicateFrom,
            out _,
            typesToInspect);
    }




















    public static void DuplicateFromOnlyReferenced<TMod, TModGetter>(
        this TMod modToDuplicateInto,
        ILinkCache<TMod, TModGetter> linkCache,
        ModKey modKeyToDuplicateFrom,
        out Dictionary<FormKey, FormKey> mapping,
        params Type[] typesToInspect)
        where TModGetter : class, IModGetter
        where TMod : class, TModGetter, IMod
    {
        if (modKeyToDuplicateFrom == modToDuplicateInto.ModKey)
        {
            throw new ArgumentException("Cannot pass the target mod's Key as the one to extract and self contain");
        }


        HashSet<IFormLinkGetter> identifiedLinks = new();
        HashSet<FormKey> passedLinks = new();
        var implicits = Implicits.Get(modToDuplicateInto.GameRelease);

        void AddAllLinks(IFormLinkGetter link)
        {
            if (link.FormKey.IsNull) return;
            if (!passedLinks.Add(link.FormKey)) return;
            if (implicits.RecordFormKeys.Contains(link.FormKey)) return;

            if (link.FormKey.ModKey == modKeyToDuplicateFrom)
            {
                identifiedLinks.Add(link);
            }

            if (!linkCache.TryResolve(link.FormKey, link.Type, out var linkRec))
            {
                return;
            }

            foreach (var containedLink in linkRec.EnumerateFormLinks())
            {
                if (containedLink.FormKey.ModKey != modKeyToDuplicateFrom) continue;
                AddAllLinks(containedLink);
            }
        }

        var enumer = typesToInspect == null || typesToInspect.Length == 0
            ? modToDuplicateInto.EnumerateMajorRecords()
            : typesToInspect.SelectMany(x => modToDuplicateInto.EnumerateMajorRecords(x));
        foreach (var rec in enumer)
        {
            AddAllLinks(new FormLinkInformation(rec.FormKey, rec.Registration.GetterType));
        }


        mapping = new();
        foreach (var identifiedRec in identifiedLinks)
        {
            if (!linkCache.TryResolveContext(identifiedRec.FormKey, identifiedRec.Type, out var rec))
            {
                throw new KeyNotFoundException($"Could not locate record to make self contained: {identifiedRec}");
            }

            var dup = rec.DuplicateIntoAsNewRecord(modToDuplicateInto, rec.Record.EditorID);
            mapping[rec.Record.FormKey] = dup.FormKey;



            modToDuplicateInto.Remove(identifiedRec.FormKey, identifiedRec.Type);
        }


        modToDuplicateInto.RemapLinks(mapping);
    }
}