namespace Mutagen.Bethesda.Fallout4;




public interface IHasDestructible : IHasDestructibleGetter
{
    new Destructible? Destructible { get; set; }
}




public interface IHasDestructibleGetter
{
    IDestructibleGetter? Destructible { get; }
}