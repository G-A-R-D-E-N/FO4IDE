using Mutagen.Bethesda.Strings;

namespace Mutagen.Bethesda.Plugins.Aspects;

public interface ITranslatedNamed : ITranslatedNamedRequired, ITranslatedNamedGetter, INamed
{

    new TranslatedString? Name { get; set; }
}

public interface ITranslatedNamedGetter : ITranslatedNamedRequiredGetter, INamedGetter
{

    new ITranslatedStringGetter? Name { get; }
}