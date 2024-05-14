using System.Text;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

//Expected Format for itemName Class.Name/UnformattedAnchor
public class LinkItemBuilder(string itemName, string baseRelativePath)
{
    private readonly StringBuilder _itemStringBuilder = new($"[{itemName}](");
    private readonly string _itemName = itemName;
    private readonly string _baseRelativePath = baseRelativePath;

    public void BuildLinkToType()
    {
        _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Types", _baseRelativePath))
            .Append('#')
            .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
            .Append(')');
    }

    public void BuildLinkToAttribute()
    {
        _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Attributes", _baseRelativePath))
            .Append('#')
            .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
            .Append(')');
    }

    public void BuildLinkToEnum()
    {
        _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Enums", _baseRelativePath))
            .Append('#')
            .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
            .Append(')');
    }

    public void BuildLinkToRecord()
    {
        _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Records", _baseRelativePath))
            .Append('#')
            .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
            .Append(')');
    }

    public void BuildLinkToAssociation()
    {
        _itemStringBuilder.Append(LinkHelpers.CreateRelativeFilepath(_itemName.Split('/').First(), "Associations", _baseRelativePath))
            .Append('#')
            .Append(LinkHelpers.FormatAnchor(_itemName.Split('/').Last()))
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