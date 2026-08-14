using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Order.DI;
using Noggog;

namespace Mutagen.Bethesda.Archives.DI;

public interface IArchiveListingDetailsProvider
{
    bool Contains(FileName fileName);
    bool IsIni(FileName fileName);
    IComparer<FileName> GetComparerFor(ModKey? modKey);
}

public class CachedArchiveListingDetailsProvider : IArchiveListingDetailsProvider
{
    private readonly IGetArchiveIniListings _getArchiveIniListings;
    private readonly Lazy<Payload> _payload;

    private class Payload
    {
        public required IReadOnlyDictionary<FileName, int> ListedPriority { get; init; }
        public required IReadOnlySet<FileName> IniSet { get; init; }
        public required IReadOnlyDictionary<FileName, int> IniPriority { get; init; }
        public required IReadOnlySet<FileName> AllSet { get; init; }
    }
    
    public CachedArchiveListingDetailsProvider(
        ILoadOrderListingsProvider listingsProvider,
        IGetArchiveIniListings getArchiveIniListings,
        IArchiveNameFromModKeyProvider archiveNameFromModKeyProvider)
    {
        _getArchiveIniListings = getArchiveIniListings;
        _payload = new Lazy<Payload>(() =>
        {
            var ini = new List<FileName>(_getArchiveIniListings.TryGet().EmptyIfNull());
            // FO4RecordEditor patch: a missing load order listing must not be fatal.
            //
            // listingsProvider.Get() resolves the game-managed load order file, which does not exist
            // off Windows (no %LocalAppData%\<Game>\Plugins.txt), so it throws
            // InvalidOperationException there. This Lazy is reached while resolving a localized
            // record's strings -- which for Fallout 4 happens during PARSE, not just when a name is
            // read -- so the throw made merely walking a vanilla record fatal, and it surfaced as
            // whole features (search, referenced-by, record views) reporting nothing found.
            //
            // Degrading to an empty list is safe: this set only supplies archive ORDERING and the
            // modKey-less Contains() path. Applicability by name ("<ModName> - *.ba2") is unaffected,
            // so strings still resolve out of the game archives and record names keep working.
            // The ini half above is already defensive in upstream (TryGet + EmptyIfNull).
            List<FileName> listed;
            try
            {
                listed = new List<FileName>(
                    listingsProvider.Get()
                        .Where(x => x.Enabled)
                        .Select(x => x.ModKey)
                        .Select(archiveNameFromModKeyProvider.Get));
            }
            catch (Exception)
            {
                listed = new List<FileName>();
            }
            return new Payload()
            {
                IniSet = ini.ToHashSet(),
                IniPriority = Priority(ini),
                ListedPriority = Priority(listed),
                AllSet = listed.And(ini).ToHashSet(),
            };
        });
    }

    private IReadOnlyDictionary<FileName, int> Priority(IEnumerable<FileName> e)
    {
        return e
            .Distinct()
            .Reverse()
            .WithIndex()
            .ToDictionary(x => x.Item, x => x.Index);
    }

    public bool Contains(FileName fileName)
    {
        return _payload.Value.AllSet.Contains(fileName);
    }
    
    public bool IsIni(FileName fileName)
    {
        return _payload.Value.IniSet.Contains(fileName);
    }
    
    public IComparer<FileName> GetComparerFor(ModKey? modKey)
    {
        return new Comparer(modKey, this);
    }
    
    private FileName BsaWithoutSuffix(FileName fileName, out string? suffix)
    {
        var lastIndexOfDelim = fileName.String.LastIndexOf(" - ", StringComparison.OrdinalIgnoreCase);
        if (lastIndexOfDelim == -1)
        {
            suffix = null;
            return fileName;
        }
        suffix = fileName.String.Substring(lastIndexOfDelim + 3);
        return new FileName(fileName.String.Substring(0, lastIndexOfDelim));
    }
    

    private class Comparer : IComparer<FileName>
    {
        private readonly ModKey? _modKey;
        private readonly CachedArchiveListingDetailsProvider _d;
        
        public Comparer(
            ModKey? modKey,
            CachedArchiveListingDetailsProvider detailsProvider)
        {
            _modKey = modKey;
            _d = detailsProvider;
        }
        
        private int FindListedIndex(FileName fileName, out string? suffix)
        {
            if (_d._payload.Value.ListedPriority.TryGetValue(fileName, out var index))
            {
                suffix = null;
                return index;
            }
        
            var strippedFileName = _d.BsaWithoutSuffix(fileName, out suffix);
            if (_d._payload.Value.ListedPriority.TryGetValue(strippedFileName, out index))
            {
                return index;
            }

            if (_modKey.HasValue && _modKey.Value.FileName.NameWithoutExtension.Equals(strippedFileName.NameWithoutExtension, StringComparison.OrdinalIgnoreCase))
            {
                return int.MaxValue;
            }
            
            // PATCHED: an archive whose owning plugin is not in the load order used to throw
            // KeyNotFoundException here, which the sort surfaced as "Failed to compare two elements
            // in the array" and took down anything that touched the environment. A Data folder can
            // legitimately hold archives for plugins that are not enabled, so this is normal input,
            // not corruption. Unowned archives sort last (comparisons are descending by index, and
            // int.MaxValue above means "belongs to the mod being asked about").
            if (_d._payload.Value.ListedPriority.TryGetValue(strippedFileName, out var fallback))
            {
                return fallback;
            }
            return int.MinValue;
        }
        
        public int Compare(FileName x, FileName y)
        {
            var payload = _d._payload.Value;
            var iniX = payload.IniSet.Contains(x);
            var iniY = payload.IniSet.Contains(y);
            if (iniX && iniY)
            {
                return payload.IniPriority[y].CompareTo(payload.IniPriority[x]);
            }
            if (iniX || iniY)
            {
                if (iniX) return -1;
                if (iniY) return 1;
                throw new NotImplementedException();
            }
            var listedX = FindListedIndex(x, out var suffixX);
            var listedY = FindListedIndex(y, out var suffixY);
            if (listedY != listedX)
            {
                return listedY.CompareTo(listedX);
            }

            // PATCHED: two archives at the same priority with the same suffix used to throw. That is
            // reachable now that unowned archives share int.MinValue, and a comparer must return a
            // total order regardless, so fall back to the file name.
            if (suffixX == suffixY)
            {
                return String.Compare(x.String, y.String, StringComparison.InvariantCultureIgnoreCase);
            }
            if (suffixX == null) return -1;
            if (suffixY == null) return 1;
            return String.Compare(suffixY, suffixX, StringComparison.InvariantCulture);
        }
    }
}