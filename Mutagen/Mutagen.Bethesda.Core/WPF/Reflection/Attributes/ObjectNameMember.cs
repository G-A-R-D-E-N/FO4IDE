namespace Mutagen.Bethesda.WPF.Reflection.Attributes;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = true)]
public class ObjectNameMember : Attribute
{
    public string Name { get; }

    public ObjectNameMember(string name)
    {
        Name = name;
    }
}