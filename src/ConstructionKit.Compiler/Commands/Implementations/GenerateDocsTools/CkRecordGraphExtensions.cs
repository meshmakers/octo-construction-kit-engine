using System.Text;
using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

internal static class CkRecordGraphExtensions
{
    public static async Task DrawRecord(this CkRecordGraph ckRecordGraph, StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync($"| {ckRecordGraph.CkRecordId.SemanticVersionedFullName} | " +
                                        $"{ckRecordGraph.DrawAttributeList((a) => a.AttributeName)} | " +
                                        $"{ckRecordGraph.DrawAttributeList((a) => a.IsOptional.ToString())} | " +
                                        $"{ckRecordGraph.DrawAttributeAutoIncrementReference()} | " +
                                        $"{ckRecordGraph.DrawAttributeAutoCompleteValues()} | " +
                                        $"{ckRecordGraph.DrawAttributeList((a) => a.CkAttributeId.SemanticVersionedFullName)} |");
    }

    private static string DrawAttributeList(this CkRecordGraph ckRecordGraph, Func<CkTypeAttributeDto, string> valueGetter)
    {
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
        foreach (var attribute in ckRecordGraph.DefinedAttributes)
        {
            stringBuilder.Append("<li>");
            stringBuilder.Append(valueGetter(attribute));
            stringBuilder.Append("</li>");
        }

        stringBuilder.Append("</ul>");

        return stringBuilder.ToString();
    }


    private static string DrawAttributeAutoIncrementReference(this CkRecordGraph ckRecordGraph)
    {
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
        foreach (var attribute in ckRecordGraph.DefinedAttributes)
        {
            stringBuilder.Append("<li>");
            stringBuilder.Append($"{attribute.AutoIncrementReference}");
            stringBuilder.Append("</li>");
        }

        stringBuilder.Append("</ul>");

        return stringBuilder.ToString();
    }
    
    private static string DrawAttributeAutoCompleteValues(this CkRecordGraph ckRecordGraph)
    {
        StringBuilder stringBuilder = new();
        stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
        foreach (var attribute in ckRecordGraph.DefinedAttributes)
        {
            if (attribute.AutoCompleteValues == null) continue;
            stringBuilder.Append("<li>");
            stringBuilder.Append("<ul style={{ listStyleType: \"none\" }}>");
            foreach (var autoCompleteValue in attribute.AutoCompleteValues)
            {
                stringBuilder.Append("<li>");
                stringBuilder.Append($"{autoCompleteValue}");
                stringBuilder.Append("</li>");
            }

            stringBuilder.Append("</ul>");
            stringBuilder.Append("</li>");
        }

        stringBuilder.Append("</ul>");

        return stringBuilder.ToString();
    }
}