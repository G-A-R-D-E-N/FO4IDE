using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using System.Diagnostics.CodeAnalysis;

namespace Mutagen.Bethesda.Plugins.Cache
{
    public interface IModContext
    {
        ModKey ModKey { get; }
        IModContext? Parent { get; }
        object? Record { get; }
    }

    public interface IModContext<out T> : IModContext
    {
        new T Record { get; }
        bool TryGetParentSimpleContext<TTargetGetter>([MaybeNullWhen(false)] out IModContext<TTargetGetter> parent);
    }

    public interface IModContext<TMod, TModGetter, out TTarget, out TTargetGetter> : IModContext<TTargetGetter>
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod
        where TTarget : TTargetGetter
        where TTargetGetter : notnull
    {
        TTarget GetOrAddAsOverride(TMod mod);
        TTarget DuplicateIntoAsNewRecord(TMod mod, FormKey? formKey = null);
        TTarget DuplicateIntoAsNewRecord(TMod mod, string? editorID);
        bool TryGetParentContext<TScopedTarget, TScopedTargetGetter>([MaybeNullWhen(false)] out IModContext<TMod, TModGetter, TScopedTarget, TScopedTargetGetter> parent)
            where TScopedTarget : TScopedTargetGetter
            where TScopedTargetGetter : notnull;
    }

    public sealed class ModContext<T> : IModContext<T>
    {
        public ModKey ModKey { get; set; }

        public IModContext? Parent { get; set; }

        public T Record { get; set; }
        object? IModContext.Record => Record;

        public ModContext(ModKey modKey, IModContext? parent, T record)
        {
            ModKey = modKey;
            Parent = parent;
            Record = record;
        }

        public bool TryGetParentSimpleContext<TScopedTargetGetter>([MaybeNullWhen(false)] out IModContext<TScopedTargetGetter> parent)
        {
            var targetContext = Parent;
            while (targetContext != null)
            {
                if (targetContext.Record is TScopedTargetGetter)
                {
                    parent = (IModContext<TScopedTargetGetter>)targetContext;
                    return true;
                }
                targetContext = targetContext.Parent;
            }
            parent = default;
            return false;
        }
    }

    public sealed class ModContext<TMod, TModGetter, TTarget, TTargetGetter> : IModContext<TMod, TModGetter, TTarget, TTargetGetter>
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod
        where TTarget : TTargetGetter
        where TTargetGetter : notnull
    {
        private readonly Func<TMod, TTargetGetter, TTarget> _getOrAddAsOverride;
        private readonly Func<TMod, TTargetGetter, string?, FormKey?, TTarget> _duplicateInto;

        public TTargetGetter Record { get; }
        object IModContext.Record => Record;

        public ModKey ModKey { get; }

        public IModContext? Parent { get; }

        public ModContext(
            ModKey modKey,
            TTargetGetter record,
            Func<TMod, TTargetGetter, TTarget> getOrAddAsOverride,
            Func<TMod, TTargetGetter, string?, FormKey?, TTarget> duplicateInto,
            IModContext? parent = null)
        {
            ModKey = modKey;
            Record = record;
            _getOrAddAsOverride = getOrAddAsOverride;
            _duplicateInto = duplicateInto;
            Parent = parent;
        }

        public static implicit operator TTargetGetter(ModContext<TMod, TModGetter, TTarget, TTargetGetter> context)
        {
            return context.Record;
        }

        public TTarget GetOrAddAsOverride(TMod mod)
        {
            try
            {
                return _getOrAddAsOverride(mod, Record);
            }
            catch (Exception ex)
            {
                var rec = Record as IMajorRecordGetter;
                RecordException.EnrichAndThrow(ex, ModKey, rec);
                throw;
            }
        }

        public bool TryGetParentContext<TScopedTarget, TScopedTargetGetter>([MaybeNullWhen(false)] out IModContext<TMod, TModGetter, TScopedTarget, TScopedTargetGetter> parent)
            where TScopedTarget : TScopedTargetGetter
            where TScopedTargetGetter : notnull
        {
            var targetContext = Parent;
            while (targetContext != null)
            {
                if (targetContext.Record is TScopedTargetGetter)
                {
                    parent = (ModContext<TMod, TModGetter, TScopedTarget, TScopedTargetGetter>)targetContext;
                    return true;
                }
                targetContext = targetContext.Parent;
            }
            parent = default;
            return false;
        }

        public bool TryGetParentSimpleContext<TScopedTargetGetter>([MaybeNullWhen(false)] out IModContext<TScopedTargetGetter> parent)
        {
            var targetContext = Parent;
            while (targetContext != null)
            {
                if (targetContext.Record is TScopedTargetGetter)
                {
                    parent = (IModContext<TScopedTargetGetter>)targetContext;
                    return true;
                }
                targetContext = targetContext.Parent;
            }
            parent = default;
            return false;
        }

        public TTarget DuplicateIntoAsNewRecord(TMod mod, string? editorID)
        {
            return _duplicateInto(mod, Record, editorID, default(FormKey?));
        }

        public TTarget DuplicateIntoAsNewRecord(TMod mod, FormKey? formKey = null)
        {
            return _duplicateInto(mod, Record, default(string?), formKey);
        }
    }

    internal sealed class ModContextCaster<TMod, TModGetter, TTarget, TTargetGetter, RTarget, RTargetGetter> : IModContext<TMod, TModGetter, RTarget, RTargetGetter>
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod
        where TTarget : TTargetGetter
        where RTarget : TTarget, RTargetGetter
        where RTargetGetter : TTargetGetter
        where TTargetGetter : notnull
    {
        private readonly IModContext<TMod, TModGetter, TTarget, TTargetGetter> _context;

        public ModKey ModKey => _context.ModKey;

        public IModContext? Parent => _context.Parent;

        object? IModContext.Record => _context.Record;

        public RTargetGetter Record => (RTargetGetter)_context.Record;

        public ModContextCaster(IModContext<TMod, TModGetter, TTarget, TTargetGetter> source)
        {
            _context = source;
        }

        public RTarget GetOrAddAsOverride(TMod mod)
        {
            return (RTarget)_context.GetOrAddAsOverride(mod);
        }

        public RTarget DuplicateIntoAsNewRecord(TMod mod, string? editorID)
        {
            return (RTarget)_context.DuplicateIntoAsNewRecord(mod, editorID);
        }

        public RTarget DuplicateIntoAsNewRecord(TMod mod, FormKey? formKey = null)
        {
            return (RTarget)_context.DuplicateIntoAsNewRecord(mod, formKey);
        }

        public bool TryGetParentContext<TScoped, TScopedGetter>(
            [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, TScoped, TScopedGetter> parent)
            where TScoped : TScopedGetter
            where TScopedGetter : notnull
        {
            return _context.TryGetParentContext(out parent);
        }

        public bool TryGetParentSimpleContext<TScopedGetter>(
            [MaybeNullWhen(false)] out IModContext<TScopedGetter> parent)
        {
            return _context.TryGetParentSimpleContext(out parent);
        }
    }

    internal sealed class SimpleModContextCaster<TGetter, RGetter> : IModContext<RGetter>
        where RGetter : TGetter
        where TGetter : notnull
    {
        private readonly IModContext<TGetter> _context;

        public ModKey ModKey => _context.ModKey;

        public IModContext? Parent => _context.Parent;

        object? IModContext.Record => _context.Record;

        public RGetter Record => (RGetter)_context.Record;

        public SimpleModContextCaster(IModContext<TGetter> source)
        {
            _context = source;
        }

        public bool TryGetParentSimpleContext<TTargetMajorGetter>(
            [MaybeNullWhen(false)] out IModContext<TTargetMajorGetter> parent)
        {
            return _context.TryGetParentSimpleContext(out parent);
        }
    }

    internal sealed class GroupModContext<TMod, TModGetter, TMajor, TMajorGetter> : IModContext<TMod, TModGetter, TMajor, TMajorGetter>
        where TModGetter : IModGetter
        where TMod : TModGetter, IMod
        where TMajor : class, IMajorRecordInternal, TMajorGetter
        where TMajorGetter : class, IMajorRecordGetter
    {
        private readonly Func<TMod, IGroup<TMajor>> _group;
        private readonly Func<TModGetter, IGroupGetter<TMajorGetter>> _groupGetter;

        public TMajorGetter Record { get; }

        public ModKey ModKey { get; }

        public IModContext? Parent => null;

        object? IModContext.Record => Record;

        public GroupModContext(
            ModKey modKey,
            TMajorGetter record,
            Func<TMod, IGroup<TMajor>> group,
            Func<TModGetter, IGroupGetter<TMajorGetter>> groupGetter)
        {
            ModKey = modKey;
            Record = record;
            _group = group;
            _groupGetter = groupGetter;
        }

        public TMajor DuplicateIntoAsNewRecord(TMod mod, string? editorID)
        {
            return _group(mod).DuplicateInAsNewRecord(Record, editorID);
        }

        public TMajor DuplicateIntoAsNewRecord(TMod mod, FormKey? formKey = null)
        {
            return _group(mod).DuplicateInAsNewRecord(Record, formKey);
        }

        public TMajor GetOrAddAsOverride(TMod mod)
        {
            return _group(mod).GetOrAddAsOverride(Record);
        }

        public TMajor? TryRetrieve(TMod mod)
        {
            return _group(mod).TryGetValue(Record.FormKey);
        }

        public TMajorGetter? TryRetrieveGetter(TModGetter mod)
        {
            return _groupGetter(mod).TryGetValue(Record.FormKey);
        }

        bool IModContext<TMod, TModGetter, TMajor, TMajorGetter>.
            TryGetParentContext<TTargetMajorSetter, TTargetMajorGetter>(
                [MaybeNullWhen(false)] out IModContext<TMod, TModGetter, TTargetMajorSetter, TTargetMajorGetter> parent)
        {
            parent = default;
            return false;
        }

        public bool TryGetParentSimpleContext<TTargetMajorGetter>(
            [MaybeNullWhen(false)] out IModContext<TTargetMajorGetter> parent)
        {
            parent = default;
            return false;
        }
    }
}
