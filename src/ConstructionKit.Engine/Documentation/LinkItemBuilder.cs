using System.Text;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

//Expected Format for itemName Class.Name/UnformattedAnchor
internal class LinkItemBuilder(string itemName, string baseRelativePath, ILinkHelpers linkHelpers) : ILinkItemBuilder
{
    private readonly StringBuilder _itemStringBuilder = new($"[{itemName}](");

    public void BuildLinkToType()
    {
        _itemStringBuilder.Append(linkHelpers.CreateRelativeFilepath(itemName.Split('/').First(), "Types", baseRelativePath))
            .Append('#')
            .Append(linkHelpers.FormatAnchor(itemName.Split('/').Last()))
            .Append(')');
    }

    public void BuildLinkToAttribute()
    {
        _itemStringBuilder.Append(linkHelpers.CreateRelativeFilepath(itemName.Split('/').First(), "Attributes", baseRelativePath))
            .Append('#')
            .Append(linkHelpers.FormatAnchor(itemName.Split('/').Last()))
            .Append(')');
    }

    public void BuildLinkToEnum()
    {
        _itemStringBuilder.Append(linkHelpers.CreateRelativeFilepath(itemName.Split('/').First(), "Enums", baseRelativePath))
            .Append('#')
            .Append(linkHelpers.FormatAnchor(itemName.Split('/').Last()))
            .Append(')');
    }

    public void BuildLinkToRecord()
    {
        _itemStringBuilder.Append(linkHelpers.CreateRelativeFilepath(itemName.Split('/').First(), "Records", baseRelativePath))
            .Append('#')
            .Append(linkHelpers.FormatAnchor(itemName.Split('/').Last()))
            .Append(')');
    }

    public void BuildLinkToAssociation()
    {
        _itemStringBuilder.Append(linkHelpers.CreateRelativeFilepath(itemName.Split('/').First(), "Associations", baseRelativePath))
            .Append('#')
            .Append(linkHelpers.FormatAnchor(itemName.Split('/').Last()))
            .Append(')');
    }

    public void BuildLinkToVersionHistory()
    {
        _itemStringBuilder.Append("./VersionHistory")
            .Append(')')
            .Insert(0, "#### ");
    }

    public override string ToString()
    {
        return _itemStringBuilder.ToString();
    }
}