using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;

internal static class CkTypeGraphExtensions
{
    private static string GetClassName(this CkId<CkTypeId> ckTypeGraph)
    {
        var fullName = ckTypeGraph.Key.SemanticVersionedFullName;

        var sanitizedFullName = GetName(ckTypeGraph) + "[\"" + fullName + "\"]";

        return sanitizedFullName;
    }

    private static string GetName(this CkId<CkTypeId> ckTypeGraph)
    {
        var fullName = ckTypeGraph.Key.SemanticVersionedFullName;

        var sanitizedFullName = fullName.Replace(".", "");

        return sanitizedFullName;
    }

    public static async Task DrawClass(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        await outputFile.WriteAsync($"class {ckTypeGraph.CkTypeId.GetClassName()}");

        //for formatting check if attributes defined
        if (ckTypeGraph.DefinedAttributes.Count != 0)
        {
            await outputFile.WriteLineAsync("{");

            foreach (var attribute in ckTypeGraph.DefinedAttributes)
            {
                await outputFile.WriteLineAsync($"+{attribute.AttributeName} : {attribute.AttributeName.GetTypeCode()}");
            }

            await outputFile.WriteLineAsync("}");
        }
        else
        {
            await outputFile.WriteLineAsync();
        }
    }

    public static async Task DrawInheritance(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        //Checks for Inheritance In BaseTypes and Creates Arrows
        if (ckTypeGraph.BaseTypes.Count != 0)
        {
            await outputFile.WriteLineAsync(
                $"{ckTypeGraph.CkTypeId.GetName()} --|> " +
                $"{ckTypeGraph.BaseTypes.First(x => x.BaseTypeDepthIndex == 0).BaseCkTypeId.GetName()}");
        }
    }

    public static async Task DrawAssociations(this CkTypeGraph ckTypeGraph, StreamWriter outputFile,
        IEnumerable<CkAssociationRoleGraph> typeAssociations)
    {
        if (ckTypeGraph.Associations.DefinedAssociations.Count != 0)
        {
            var ckAssociationRoleGraphs = typeAssociations as CkAssociationRoleGraph[] ?? typeAssociations.ToArray();
            
            foreach (var association in ckTypeGraph.Associations.Out.Owned)
            {
                //check if ID's for associations match to create adequate multiplicities
                foreach (var item in ckAssociationRoleGraphs)
                {
                    if (association.CkRoleId.FullName != item.CkRoleId.FullName) continue;
                    var outboundMultiplicityConversion = FormatOutboundMultiplicity(item);
                    var inboundMultiplicityConversion = FormatInboundMultiplicity(item);
                    await outputFile.WriteLineAsync(
                        $"{association.OriginCkTypeId.GetName()} \"{outboundMultiplicityConversion}\" -->" +
                        $" \"{inboundMultiplicityConversion}\" {association.TargetCkTypeId.GetName()} : " +
                        $"{association.CkRoleId.SemanticVersionedFullName}");
                }
            }
        }
    }

    private static string FormatInboundMultiplicity(CkAssociationRoleGraph item)
    {
        var inboundMultiplicity = item.InboundMultiplicity;

        var inboundMultiplicityConversion = inboundMultiplicity switch
        {
            Contracts.DataTransferObjects.MultiplicitiesDto.ZeroOrOne => "0..1",
            Contracts.DataTransferObjects.MultiplicitiesDto.One => "1",
            _ => "n"
        };

        return inboundMultiplicityConversion;
    }

    private static string FormatOutboundMultiplicity(CkAssociationRoleGraph item)
    {
        var outboundMultiplicity = item.OutboundMultiplicity;
        var outboundMultiplicityConversion = "n";

        if (outboundMultiplicity == Contracts.DataTransferObjects.MultiplicitiesDto.ZeroOrOne)
        {
            outboundMultiplicityConversion = "0..1";
        }
        else if (outboundMultiplicity == Contracts.DataTransferObjects.MultiplicitiesDto.One)
        {
            outboundMultiplicityConversion = "1";
        }

        return outboundMultiplicityConversion;
    }

    public static async Task DrawNamespaces(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        await GetNamespaceName(ckTypeGraph, outputFile);

        await outputFile.WriteLineAsync($"class {ckTypeGraph.CkTypeId.GetName()}");

        await outputFile.WriteLineAsync($"}}");
    }

    private static async Task GetNamespaceName(CkTypeGraph ckTypeGraph, StreamWriter outputFile)
    {
        await outputFile.WriteLineAsync($"namespace {ckTypeGraph.CkTypeId.ModelId.ModelId.Replace(".", "")} {{");
    }

    public static async Task LinkToType(this CkTypeGraph ckTypeGraph, StreamWriter outputFile, string baseRelativePath)
    {
        await outputFile.WriteAsync($"link {ckTypeGraph.CkTypeId.GetName()} \"");
        await outputFile.WriteAsync(LinkHelpers.CreateRelativeFilepath(ckTypeGraph.CkTypeId.ModelId.FullName, "Types", baseRelativePath));
        await outputFile.WriteLineAsync($"#{ckTypeGraph.CreateAnchor()}\"");
    }

    private static string CreateAnchor(this CkTypeGraph ckTypeGraph)
    {
        return ckTypeGraph.CkTypeId.Key.TypeId.ToLower();
    }
}