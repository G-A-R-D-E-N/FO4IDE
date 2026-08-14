using Loqui;
using Mutagen.Bethesda.Plugins.Cache.Internals;
using Mutagen.Bethesda.Plugins.Records.Mapping;
using Noggog;

namespace Mutagen.Bethesda.Plugins;




public static class Warmup
{
    private static object _lock = new();
    private static bool _warmedUp = false;

    private static List<GameCategory> _registrations = new();





    public static IReadOnlyList<GameCategory> Init()
    {
        lock (_lock)
        {
            if (_warmedUp) return _registrations;
            _warmedUp = true;

            List<IProtocolRegistration> protocols = new()
            {
                new ProtocolDefinition_Bethesda()
            };

            foreach (var category in Enums<GameCategory>.Values)
            {
                try
                {
                    var assemblyName = $"Mutagen.Bethesda.{category}";
                    var obj = Activator.CreateInstance(
                        assemblyName,
                        $"Loqui.ProtocolDefinition_{category}");
                    var regis = obj?.Unwrap() as IProtocolRegistration;
                    if (regis == null) continue;
                    protocols.Add(regis);
                    _registrations.Add(category);
                }
                catch
                {
                }
            }

            Initialization.SpinUp(protocols.ToArray());
            GetterTypeMapping.Warmup();
            MetaInterfaceMapping.Warmup();
            OverrideMaskRegistrations.Warmup();

            return _registrations;
        }
    }
}
