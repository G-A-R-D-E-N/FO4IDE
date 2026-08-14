using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Aspects;
using Mutagen.Bethesda.Plugins.Cache;
using Noggog;
using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda
{
    namespace Plugins.Aspects
    {

        public interface IKeyworded<TKeyword> : IKeywordedGetter<TKeyword>, IMajorRecordQueryable
            where TKeyword : class, IKeywordCommonGetter
        {
            new ExtendedList<IFormLinkGetter<TKeyword>>? Keywords { get; set; }
        }

        public interface IKeywordedGetter : IMajorRecordQueryableGetter
        {
            IReadOnlyList<IFormLinkGetter<IKeywordCommonGetter>>? Keywords { get; }
        }

        public interface IKeywordedGetter<TKeyword> : IKeywordedGetter
            where TKeyword : class, IKeywordCommonGetter
        {
            new IReadOnlyList<IFormLinkGetter<TKeyword>>? Keywords { get; }
        }
    }

    public static class IKeywordedExt
    {

        public static bool HasKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            FormKey keywordKey)
            where TKeyword : class, IKeywordCommonGetter
        {
            return keyworded.Keywords?.Any(x => x.FormKey == keywordKey) ?? false;
        }

        public static bool TryResolveKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            FormKey keywordKey,
            ILinkCache cache,
            [MaybeNullWhen(false)] out TKeyword keyword)
            where TKeyword : class, IKeywordCommonGetter
        {
            if (!HasKeyword(keyworded, keywordKey))
            {
                keyword = default;
                return false;
            }
            return cache.TryResolve(keywordKey, out keyword);
        }

        public static bool HasKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            IFormLinkGetter<TKeyword> keywordLink)
            where TKeyword : class, IKeywordCommonGetter
        {
            return HasKeyword(keyworded, keywordLink.FormKey);
        }

        public static bool TryResolveKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            IFormLinkGetter<TKeyword> keywordLink,
            ILinkCache cache,
            [MaybeNullWhen(false)] out TKeyword keyword)
            where TKeyword : class, IKeywordCommonGetter
        {
            return TryResolveKeyword(keyworded, keywordLink.FormKey, cache, out keyword);
        }

        public static bool HasKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            TKeyword keyword)
            where TKeyword : class, IKeywordCommonGetter
        {
            return keyworded.HasKeyword(keyword.FormKey);
        }

        public static bool TryResolveKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            string editorID,
            ILinkCache cache,
            [MaybeNullWhen(false)] out TKeyword keyword,
            StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
            where TKeyword : class, IKeywordCommonGetter
        {

            if (keyworded.Keywords == null)
            {
                keyword = default;
                return false;
            }
            foreach (var keywordForm in keyworded.Keywords.Select(link => link.FormKey))
            {
                if (cache.TryResolve<TKeyword>(keywordForm, out keyword)
                    && (keyword.EditorID?.Equals(editorID, stringComparison) ?? false))
                {
                    return true;
                }
            }

            keyword = default;
            return false;
        }

        public static bool HasKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            string editorID,
            ILinkCache cache,
            StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
            where TKeyword : class, IKeywordCommonGetter
        {
            return TryResolveKeyword(keyworded, editorID, cache, out _, stringComparison);
        }

        public static bool HasAnyKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            IEnumerable<FormKey> keywordKeys)
            where TKeyword : class, IKeywordCommonGetter
        {
            return keyworded.Keywords?.IntersectBy(keywordKeys, x => x.FormKey).Any() ?? false;
        }

        public static bool HasAnyKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            IEnumerable<IFormLinkGetter<TKeyword>> keywordLink)
            where TKeyword : class, IKeywordCommonGetter
        {
            return HasAnyKeyword(keyworded, keywordLink.Select(x => x.FormKey));
        }

        public static bool HasAnyKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            IEnumerable<TKeyword> keywords)
            where TKeyword : class, IKeywordCommonGetter
        {
            return keyworded.HasAnyKeyword(keywords.Select(x => x.FormKey));
        }

        public static bool HasAnyKeyword<TKeyword>(
            this IKeywordedGetter<TKeyword> keyworded,
            IEnumerable<string> editorIDs,
            ILinkCache cache,
            StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
            where TKeyword : class, IKeywordCommonGetter
        {

            if (keyworded.Keywords == null)
            {
                return false;
            }
            foreach (var keywordForm in keyworded.Keywords.Select(link => link.FormKey))
            {
                if (cache.TryResolve<TKeyword>(keywordForm, out var keyword))
                {
                    var kwEditorID = keyword.EditorID;
                    if (editorIDs.Any(editorID => kwEditorID?.Equals(editorID, stringComparison) ?? false))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
