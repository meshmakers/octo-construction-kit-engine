using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

/// <summary>
///  Interface for a Mermaid Generator
/// </summary>
public interface IMermaidGenerator
{
    /// <summary>
    /// Generetes the Mermaid Diagram with Extras added for use with Docusaurus
    /// </summary>
    /// <param name="modelGraph">The Model that the Diagram is generated from</param>
    /// <param name="documentPath">Path where the generated Diagram is written</param>
    /// <param name="ckModelId">Used to determine position in file tree</param>
    /// <param name="versionNumber">Version of the Model used</param>
    /// <param name="linkPathRoot"></param>
    /// ///
    /// <returns></returns>
    public Task GenerateMermaidTextOutput(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId, string? versionNumber, 
        string linkPathRoot);

    /// <summary>
    /// Generates Mermaid Diagram for given CkModelGraph
    /// </summary>
    /// <param name="modelGraph">The Model that the Diagram is generated from</param>
    /// <param name="outputPath">The path where the Diagram is generated, include extension e.g. "diagram.txt"</param>
    public Task GenerateMermaidDiagram(CkModelGraph modelGraph, string outputPath);

    /// <summary>
    /// Generates Mermaid Diagram for given CkModelGraph
    /// </summary>
    /// <param name="modelGraph">The Model that the Diagram is generated from</param>
    /// <param name="documentPath">Used to find relative Path and Directory Path</param>
    /// <param name="ckModelId">Used to build directory</param>
    /// <param name="outputFile">The file where the Diagram is generated</param>
    /// <param name="directoryPath"></param>
    /// <returns></returns>
    public Task GenerateMermaidDiagram(CkModelGraph modelGraph, string documentPath, CkModelId ckModelId, StreamWriter outputFile,
        string directoryPath);
}