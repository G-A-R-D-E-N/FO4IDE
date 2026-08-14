namespace Mutagen.Bethesda.Plugins.Binary.Parameters;







public abstract class AMinimumFormIdOption
{
    public static AutomaticLowerFormIdRangeOption Automatic { get; } = new AutomaticLowerFormIdRangeOption();
    public static ForceLowerFormIdRangeOption Force(bool? on)
    {
        return new ForceLowerFormIdRangeOption()
        {
            ForceLowerRangeSetting = on,
        };
    }
}





public class AutomaticLowerFormIdRangeOption : AMinimumFormIdOption
{
}




public class ForceLowerFormIdRangeOption : AMinimumFormIdOption
{



    public bool? ForceLowerRangeSetting { get; init; }
}