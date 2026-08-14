using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Noggog;

namespace Mutagen.Bethesda.Plugins.Analysis.DI;

public class MultiModFileSplitter : IMultiModFileSplitter
{
    internal class EquatableModKeySet : IEquatable<EquatableModKeySet>
    {
        private readonly ModKey[] _modKeys;
        private readonly int _hash;

        public EquatableModKeySet(IEnumerable<ModKey> modKeys)
        {
            _modKeys = modKeys.OrderBy(x => x.FileName.String).ToArray();
            _hash = GetHashCodeForModKeys();
        }

        private int GetHashCodeForModKeys()
        {
            HashCode hashCode = default;
            foreach (var modKey in _modKeys)
            {
                hashCode.Add(modKey);
            }
            return hashCode.ToHashCode();
        }

        public bool Equals(EquatableModKeySet? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return _modKeys.SequenceEqual(other._modKeys);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((EquatableModKeySet)obj);
        }

        public override int GetHashCode()
        {
            return _hash;
        }
    }




    internal class Cluster<TMod, TModGetter>
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {



        public HashSet<ModKey> Masters = new();




        public List<IModContext<TMod, TModGetter, IMajorRecord, IMajorRecordGetter>> Records = new();
    }

    private static HashSet<ModKey> GetAllMastersForRecord(
        IMajorRecordGetter record,
        ModKey except)
    {
        var result = new HashSet<ModKey>();

        result.Add(record.FormKey.ModKey);

        foreach (var formLink in record.EnumerateFormLinks(iterateNestedRecords: false))
        {
            result.Add(formLink.FormKey.ModKey);
        }

        result.Remove(except);

        return result;
    }








    private static HashSet<ModKey> GetMastersForClustering(
        IModContext<IMajorRecordGetter> rec,
        ModKey except)
    {
        var result = GetAllMastersForRecord(rec.Record, except);




        var parent = rec.Parent;
        while (parent?.Record is IMajorRecordGetter parentRecord)
        {
            var parentMasters = GetAllMastersForRecord(parentRecord, except);
            result.UnionWith(parentMasters);
            parent = parent.Parent;
        }

        return result;
    }







    private static List<Cluster<TMod, TModGetter>> GenerateClusters<TMod, TModGetter>(TMod inputMod, int limit)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        var clusters = new List<Cluster<TMod, TModGetter>>();

        var clusterLookupCache = new Dictionary<EquatableModKeySet, Cluster<TMod, TModGetter>>();

        var linkCache = inputMod.ToUntypedImmutableLinkCache();
        foreach (var rec in inputMod.EnumerateMajorRecordContexts<IMajorRecord, IMajorRecordGetter>(linkCache))
        {
            var mastersHashSet = GetMastersForClustering(rec, inputMod.ModKey);


            if (mastersHashSet.Count > limit)
            {
                throw new TooManyMastersException(
                    inputMod.ModKey,
                    mastersHashSet.ToArray());
            }

            var masters = new EquatableModKeySet(mastersHashSet);

            if (clusterLookupCache.ContainsKey(masters))
            {
                var cacheCluster = clusterLookupCache[masters];


                cacheCluster.Records.Add(rec);
                continue;
            }

            Cluster<TMod, TModGetter>? existingCluster = null;

            foreach (Cluster<TMod, TModGetter> curCluster in clusters)
            {
                var missingMasters = mastersHashSet.Except(curCluster.Masters).ToArray();

                if (curCluster.Masters.Count + missingMasters.Count() <= limit)
                {

                    curCluster.Masters.Add(missingMasters);
                    existingCluster = curCluster;
                    break;
                }
            }

            if (existingCluster == null)
            {

                var newCluster = new Cluster<TMod, TModGetter>
                {
                    Masters = mastersHashSet
                };
                newCluster.Records.Add(rec);

                clusters.Add(newCluster);
                clusterLookupCache.Add(masters, newCluster);
                continue;
            }

            existingCluster.Records.Add(rec);
            clusterLookupCache.Add(masters, existingCluster);
        }

        return clusters;
    }

    public IReadOnlyList<TMod> Split<TMod, TModGetter>(TMod inputMod, int masterLimit)
        where TMod : IMod, TModGetter, IMajorRecordContextEnumerable<TMod, TModGetter>
        where TModGetter : IModGetter
    {
        var result = new List<TMod>();
        var clusters = GenerateClusters<TMod, TModGetter>(inputMod, masterLimit);
        for (int i = 0; i < clusters.Count; i++)
        {
            var curCluster = clusters[i];
            string curFileName;
            if (i == 0)
            {

                curFileName = inputMod.ModKey.FileName;
            }
            else
            {

                curFileName = $"{inputMod.ModKey.FileName.NameWithoutExtension}_{(i + 1)}{inputMod.ModKey.FileName.Extension}";
            }

            var newMod = ModFactory<TMod>.Activator(ModKey.FromFileName(curFileName), inputMod.GameRelease);

            foreach (var context in curCluster.Records)
            {
                if (context.Record.FormKey.ModKey == inputMod.ModKey)
                {

                    context.DuplicateIntoAsNewRecord(newMod, new FormKey(newMod.ModKey, context.Record.FormKey.ID));
                }
                else
                {

                    context.GetOrAddAsOverride(newMod);
                }
            }

            result.Add(newMod);
        }

        return result;
    }
}