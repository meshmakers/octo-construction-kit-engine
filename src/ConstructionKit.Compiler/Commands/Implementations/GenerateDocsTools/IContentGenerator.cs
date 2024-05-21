using Meshmakers.Common.CommandLineParser.Commands;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

public interface IContentGenerator
{
    Task GenerateAttributesMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, string 
        documentPath, CkModelId ckModelId);

    Task GenerateEnumsMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId);

    Task GenerateRecordsMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId);

    Task GenerateTypesMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph, 
        string documentPath, CkModelId ckModelId);

    Task GenerateAssociationRolesMarkdownTable(ILogger<Command<OctoToolOptions>> logger, CkModelGraph modelGraph,
        string documentPath, CkModelId ckModelId);

    Task GenerateVersionHistory(string docPath, CkModelId ckModelId);
}