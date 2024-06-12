using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class CkTypeAttributeDtoExtensions
{
    public static async Task DrawAttribute(this CkTypeAttributeDto ckTypeAttributeDto, StreamWriter outputFile, string baseRelativePath,
        ILinkHelpers linkHelpers)
    {
        await outputFile.WriteLineAsync($"| {ckTypeAttributeDto.DrawLinkToDefinition(baseRelativePath, linkHelpers)} | " +
                                        $"{ckTypeAttributeDto.DrawAttributeAutoCompleteValues()} | " +
                                        $"{ckTypeAttributeDto.DrawAttributeAutoIncrementReference()} | " +
                                        $"{ckTypeAttributeDto.IsOptional.ToString()} |").ConfigureAwait(false);
    }

    private static string DrawAttributeAutoCompleteValues(this CkTypeAttributeDto ckTypeAttributeDto)
    {
        if (ckTypeAttributeDto.AutoCompleteValues == null) return "";
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");


        foreach (var attribute in ckTypeAttributeDto.AutoCompleteValues)
        {
            stringBuilder.Append("<li>");
            stringBuilder.Append($"{attribute}");
            stringBuilder.Append("</li>");
        }

        stringBuilder.Append("</ul>");
        return stringBuilder.ToString();

    }

    private static string DrawAttributeAutoIncrementReference(this CkTypeAttributeDto ckTypeAttributeDto)
    {
        if (ckTypeAttributeDto.AutoIncrementReference == null) return "";
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
        foreach (var attribute in ckTypeAttributeDto.AutoIncrementReference)
        {
            stringBuilder.Append("<li>");
            stringBuilder.Append($"{attribute}");
            stringBuilder.Append("</li>");
        }

        stringBuilder.Append("</ul>");

        return stringBuilder.ToString();

    }

    private static string DrawLinkToDefinition(this CkTypeAttributeDto ckTypeAttributeDto, string baseRelativePath, ILinkHelpers linkHelpers)
    {
        var builder = new LinkItemBuilder(ckTypeAttributeDto.CkAttributeId.SemanticVersionedFullName, baseRelativePath, linkHelpers);
        builder.BuildLinkToAttribute();
        return builder.ToString();
    }
}