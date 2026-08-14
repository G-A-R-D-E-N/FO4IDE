namespace Mutagen.Bethesda.Fallout4;

public interface IHasIcons : IHasIconsGetter
{
    new Icons? Icons { get; set; }
}

public interface IHasIconsGetter
{
    IIconsGetter? Icons { get; }
}