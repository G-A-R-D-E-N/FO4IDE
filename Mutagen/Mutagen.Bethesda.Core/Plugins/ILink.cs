using System.Diagnostics.CodeAnalysis;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;

namespace Mutagen.Bethesda.Plugins;

public interface ILinkIdentifier
{

    Type Type { get; }
}

public interface ILink : ILinkIdentifier
{

    bool TryGetModKey([MaybeNullWhen(false)] out ModKey modKey);

    bool TryResolveFormKey(ILinkCache cache, out FormKey formKey);

    bool TryResolveCommon(ILinkCache cache, [MaybeNullWhen(false)] out IMajorRecordGetter majorRecord);
}

public interface ILink<out TMajor> : ILink
    where TMajor : IMajorRecordGetter
{

    TMajor? TryResolve(ILinkCache cache);
}