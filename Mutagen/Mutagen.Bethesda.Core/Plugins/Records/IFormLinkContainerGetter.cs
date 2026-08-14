namespace Mutagen.Bethesda.Plugins.Records;




public interface IFormLinkContainer : IFormLinkContainerGetter
{



    void RemapLinks(IReadOnlyDictionary<FormKey, FormKey> mapping);
}




public interface IFormLinkContainerGetter
{




    IEnumerable<IFormLinkGetter> EnumerateFormLinks(bool iterateNestedRecords = true);
}