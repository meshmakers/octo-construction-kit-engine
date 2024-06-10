namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

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