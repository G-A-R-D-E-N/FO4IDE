using Mutagen.Bethesda.Strings;

namespace Mutagen.Bethesda.Plugins.Aspects;

public interface ITranslatedNamedRequired : ITranslatedNamedRequiredGetter, INamedRequired
{

    new TranslatedString Name { get; set; }
}

public interface ITranslatedNamedRequiredGetter : INamedRequiredGetter
{

    new ITranslatedStringGetter Name { get; }
}