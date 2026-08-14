using Noggog;

namespace Mutagen.Bethesda.Fallout4;

public interface IHasEffects : IFallout4MajorRecordInternal, IHasEffectsGetter
{
    new ExtendedList<Effect> Effects { get; init; }
}




public interface IHasEffectsGetter : IFallout4MajorRecordGetter
{
    IReadOnlyList<IEffectGetter> Effects { get; }
}
