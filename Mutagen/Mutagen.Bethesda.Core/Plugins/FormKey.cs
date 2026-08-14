using Mutagen.Bethesda.Plugins.Order;
using Noggog;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Mutagen.Bethesda.Plugins.Masters;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins;










[DebuggerDisplay("{ToString()}")]
public readonly struct FormKey : IEquatable<FormKey>, IComparable<FormKey>, IFormKeyGetter
{



    public const string NullStr = "Null";




    public static readonly FormKey Null = new FormKey(ModKey.Null, 0);




    public const string NoneStr = "None";




    public static readonly FormKey None = new FormKey(ModKey.Null, 0xFFFFFF);




    public readonly uint ID;




    public readonly ModKey ModKey;




    public bool IsNull => ModKey.IsNull;







    public FormKey(ModKey modKey, uint id)
    {
        ModKey = modKey;
        ID = id & 0xFFFFFF;
    }








    internal static FormKey Factory(IReadOnlySeparatedMasterPackage masterReferences, FormID formId, bool reference)
    {
        return masterReferences.GetFormKey(formId, reference: reference);
    }









    internal static FormKey Factory(IReadOnlySeparatedMasterPackage masterReferences, FormID formId, bool reference, bool maxIsNull)
    {
        if (maxIsNull && formId.Raw == uint.MaxValue)
        {
            return FormKey.None;
        }
        return masterReferences.GetFormKey(formId, reference: true);
    }

    private static bool IsDelim(char c) => c is ':' or '_';








    public static bool TryFactory(ReadOnlySpan<char> str, [MaybeNullWhen(false)]out FormKey formKey)
    {

        str = str.Trim();


        if (NullStr.AsSpan().Equals(str, StringComparison.OrdinalIgnoreCase))
        {
            formKey = Null;
            return true;
        }


        if (NoneStr.AsSpan().Equals(str, StringComparison.OrdinalIgnoreCase))
        {
            formKey = None;
            return true;
        }


        const int shortCircuitSize = 9;
        if (str.Length < shortCircuitSize)
        {
            formKey = default!;
            return false;
        }


        if (!IsDelim(str[6]))
        {
            formKey = default!;
            return false;
        }

        int delim = 6;


        if (!uint.TryParse(str.Slice(0, delim), NumberStyles.HexNumber, null, out var id))
        {
            formKey = default!;
            return false;
        }


        str = str.Slice(delim + 1);

        if (!ModKey.TryFromNameAndExtension(str, out var modKey))
        {
            if (str.Equals(NullStr, StringComparison.OrdinalIgnoreCase))
            {
                modKey = ModKey.Null;
            }
            else
            {
                formKey = default!;
                return false;
            }
        }

        formKey = new FormKey(
            modKey: modKey,
            id: id);
        return true;
    }







    public static FormKey? TryFactory(ReadOnlySpan<char> str)
    {
        if (TryFactory(str, out var formKey))
        {
            return formKey;
        }
        return default;
    }








    public static FormKey Factory(ReadOnlySpan<char> str)
    {
        if (!TryFactory(str, out var form))
        {
            throw new ArgumentException($"Malformed FormKey string: {str.ToString()}");
        }
        return form;
    }





    public override string ToString()
    {
        if (ID == 0 && ModKey.IsNull)
        {
            return NullStr;
        }

        if (None == this)
        {
            return NoneStr;
        }
        return $"{IDString()}:{ModKey}";
    }





    public string IDString()
    {
        return ID.ToString("X6");
    }





    public string ToFilesafeString()
    {
        if (ModKey.IsNull)
        {
            return "Null";
        }
        return $"{IDString()}_{ModKey}";
    }






    public override bool Equals(object? other)
    {
        if (other is not FormKey key) return false;
        return Equals(key);
    }






    public bool Equals(FormKey other)
    {
        if (!ModKey.Equals(other.ModKey)) return false;
        if (ID != other.ID) return false;
        return true;
    }





    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ModKey);
        hash.Add(ID);
        return hash.ToHashCode();
    }











    public int CompareTo(FormKey other)
    {

        if (ModKey.IsNull && other.ModKey.IsNull)
        {
            return ID.CompareTo(other.ID);
        }
        if (ModKey.IsNull)
        {
            return -1;
        }
        if (other.ModKey.IsNull)
        {
            return 1;
        }

        var modKeyComparison = string.Compare(ModKey.ToString(), other.ModKey.ToString(), StringComparison.OrdinalIgnoreCase);
        if (modKeyComparison != 0) return modKeyComparison;
        return ID.CompareTo(other.ID);
    }

    [Obsolete("Use ToLink instead")]
    public FormLink<TMajorGetter> AsLink<TMajorGetter>()
        where TMajorGetter : class, IMajorRecordGetter
    {
        return new FormLink<TMajorGetter>(this);
    }

    public FormLink<TMajorGetter> ToLink<TMajorGetter>()
        where TMajorGetter : class, IMajorRecordGetter
    {
        return new FormLink<TMajorGetter>(this);
    }

    [Obsolete("Use ToLinkGetter instead")]
    public IFormLinkGetter<TMajorGetter> AsLinkGetter<TMajorGetter>()
        where TMajorGetter : class, IMajorRecordGetter
    {
        return new FormLink<TMajorGetter>(this);
    }

    public IFormLinkGetter<TMajorGetter> ToLinkGetter<TMajorGetter>()
        where TMajorGetter : class, IMajorRecordGetter
    {
        return new FormLink<TMajorGetter>(this);
    }

    public static bool operator ==(FormKey? a, FormKey? b)
    {
        return EqualityComparer<FormKey?>.Default.Equals(a, b);
    }

    public static bool operator !=(FormKey? a, FormKey? b)
    {
        return !EqualityComparer<FormKey?>.Default.Equals(a, b);
    }

    #region Comparers





    public static Comparer<FormKey> AlphabeticalComparer(bool mastersFirst = true) => new AlphabeticalFormKeyComparer(mastersFirst);

    private class AlphabeticalFormKeyComparer : Comparer<FormKey>
    {
        private readonly bool _mastersFirst;

        public AlphabeticalFormKeyComparer(bool mastersFirst)
        {
            _mastersFirst = mastersFirst;
        }

        public override int Compare(FormKey x, FormKey y)
        {
            if (_mastersFirst
                && x.ModKey.Type != y.ModKey.Type)
            {
                return x.ModKey.Type.CompareTo(y.ModKey.Type);
            }

            var stringComp = string.Compare(x.ModKey.FileName, y.ModKey.FileName, StringComparison.OrdinalIgnoreCase);
            if (stringComp != 0) return stringComp;

            return x.ID.CompareTo(y.ID);
        }
    }










    public static Comparer<FormKey> LoadOrderComparer(
        IReadOnlyList<ModKey> loadOrder,
        Comparer<FormKey>? matchingModKeyFallback = null,
        Comparer<FormKey>? notOnLoadOrderFallback = null) =>
        new ModKeyListFormKeyComparer(loadOrder, matchingModKeyFallback: matchingModKeyFallback, notOnLoadOrderFallback: notOnLoadOrderFallback);










    public static Comparer<FormKey> LoadOrderComparer(
        IEnumerable<ModKey> loadOrder,
        Comparer<FormKey>? matchingModKeyFallback = null,
        Comparer<FormKey>? notOnLoadOrderFallback = null) =>
        new ModKeyListFormKeyComparer(loadOrder.ToList(), matchingModKeyFallback: matchingModKeyFallback, notOnLoadOrderFallback: notOnLoadOrderFallback);










    public static Comparer<FormKey> LoadOrderComparer(
        ILoadOrderGetter loadOrder,
        Comparer<FormKey>? matchingModKeyFallback = null,
        Comparer<FormKey>? notOnLoadOrderFallback = null) =>
        new ModKeyListFormKeyComparer(loadOrder.ListedOrder.ToList(), matchingModKeyFallback: matchingModKeyFallback, notOnLoadOrderFallback: notOnLoadOrderFallback);

    private class ModKeyListFormKeyComparer : Comparer<FormKey>
    {
        private readonly IReadOnlyList<ModKey> _loadOrder;
        private readonly Comparer<FormKey> _matchingModKeyFallback;
        private readonly Comparer<FormKey>? _notOnLoadOrderFallback;

        public ModKeyListFormKeyComparer(
            IReadOnlyList<ModKey> loadOrder,
            Comparer<FormKey>? matchingModKeyFallback,
            Comparer<FormKey>? notOnLoadOrderFallback)
        {
            _loadOrder = loadOrder;
            _matchingModKeyFallback = matchingModKeyFallback ?? AlphabeticalComparer(mastersFirst: false);
            _notOnLoadOrderFallback = notOnLoadOrderFallback;
        }

        public override int Compare(FormKey x, FormKey y)
        {
            if (x.ModKey != y.ModKey)
            {
                var xIndex = _loadOrder.IndexOf(x.ModKey);
                if (xIndex == -1)
                {
                    if (_notOnLoadOrderFallback != null)
                    {
                        return _notOnLoadOrderFallback.Compare(x, y);
                    }
                    throw new ArgumentOutOfRangeException($"ModKey was not on load order: {x.ModKey}");
                }
                var yIndex = _loadOrder.IndexOf(y.ModKey);
                if (yIndex == -1)
                {
                    if (_notOnLoadOrderFallback != null)
                    {
                        return _notOnLoadOrderFallback.Compare(x, y);
                    }
                    throw new ArgumentOutOfRangeException($"ModKey was not on load order: {y.ModKey}");
                }
                return xIndex.CompareTo(yIndex);
            }

            return _matchingModKeyFallback.Compare(x, y);
        }
    }









    public static Comparer<FormKey> LoadOrderComparer<TItem>(
        LoadOrder<TItem> loadOrder,
        Comparer<FormKey>? matchingFallback = null)
        where TItem : class, IModKeyed
    {
        return new ModEntryListFormKeyComparer<TItem>(loadOrder, matchingFallback);
    }

    private class ModEntryListFormKeyComparer<TItem> : Comparer<FormKey>
        where TItem : class, IModKeyed
    {
        private readonly LoadOrder<TItem> _loadOrder;
        private readonly Comparer<FormKey> _matchingFallback;

        public ModEntryListFormKeyComparer(
            LoadOrder<TItem> loadOrder,
            Comparer<FormKey>? matchingFallback)
        {
            _loadOrder = loadOrder;
            _matchingFallback = matchingFallback ?? AlphabeticalComparer(mastersFirst: false);
        }

        public override int Compare(FormKey x, FormKey y)
        {
            if (x.ModKey != y.ModKey)
            {
                var xIndex = _loadOrder.IndexOf(x.ModKey, (l, k) => l.Key.Equals(k));
                if (xIndex == -1) throw new ArgumentOutOfRangeException($"ModKey was not on load order: {x.ModKey}");
                var yIndex = _loadOrder.IndexOf(y.ModKey, (l, k) => l.Key.Equals(k));
                if (yIndex == -1) throw new ArgumentOutOfRangeException($"ModKey was not on load order: {y.ModKey}");
                return xIndex.CompareTo(yIndex);
            }

            return _matchingFallback.Compare(x, y);
        }
    }
    #endregion

    FormKey IFormKeyGetter.FormKey => this;
}