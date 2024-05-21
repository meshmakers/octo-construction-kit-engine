using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

internal static class GetValues
{
    public static IEnumerable<CkTypeGraph> GetTypes(CkModelGraph modelGraph)
    {
        return modelGraph.Types.Select(x => x.Value);
    }

    public static IEnumerable<CkAttributeGraph> GetAttributes(CkModelGraph modelGraph)
    {
        return modelGraph.Attributes.Select(x => x.Value);
    }

    public static IEnumerable<CkEnumGraph> GetEnums(CkModelGraph modelGraph)
    {
        return modelGraph.Enums.Select(x => x.Value);
    }

    public static IEnumerable<CkRecordGraph> GetRecords(CkModelGraph modelGraph)
    {
        return modelGraph.Records.Select(x => x.Value);
    }

    public static IEnumerable<CkAssociationRoleGraph> GetAssociationRoles(CkModelGraph modelGraph)
    {
        return modelGraph.AssociationRoles.Select(x => x.Value);
    }
}