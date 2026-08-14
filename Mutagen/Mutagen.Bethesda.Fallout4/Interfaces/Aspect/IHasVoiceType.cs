using Mutagen.Bethesda.Plugins;

namespace Mutagen.Bethesda.Fallout4;

public interface IHasVoiceType : IHasVoiceTypeGetter, IFallout4MajorRecordInternal
{
    IFormLinkNullable<IVoiceTypeGetter> Voice { get; }
}




public interface IHasVoiceTypeGetter : IFallout4MajorRecordGetter
{
    IFormLinkNullableGetter<IVoiceTypeGetter> Voice { get; }
}
