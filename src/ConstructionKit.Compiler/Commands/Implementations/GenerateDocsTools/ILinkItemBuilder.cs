namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

internal interface ILinkItemBuilder
{
    void BuildLinkToType();
    void BuildLinkToAttribute();
    void BuildLinkToEnum();
    void BuildLinkToRecord();
    void BuildLinkToAssociation();
    void BuildLinkToVersionHistory();
    string ToString();
}