using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

internal interface IContentGenerator
{
    Task GenerateAttributesMarkdownTable(CkModelGraph modelGraph, string 
        documentPath, CkModelId ckModelId);

    Task GenerateEnumsMarkdownTable(CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId);

    Task GenerateRecordsMarkdownTable(CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId);

    Task GenerateTypesMarkdownTable(CkModelGraph modelGraph, 
        string documentPath, CkModelId ckModelId);

    Task GenerateAssociationRolesMarkdownTable(CkModelGraph modelGraph,
        string documentPath, CkModelId ckModelId);

    Task GenerateVersionHistory(string docPath, CkModelId ckModelId);
}