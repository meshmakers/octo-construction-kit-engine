using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

/// <summary>
///  Generates Full Mermaid Diagram for given CkModelGraph, ID Determines Position in File Tree
/// </summary>
public interface IMermaidGenerator
{
    public Task GenerateMermaidTextOutput(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId);
}