using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

public class MermaidGenerator(CkModelGraph ckModelGraph, string sourcePath, string destinationPath, CkModelId ckModelId)
{
    private CkModelGraph _ckModelGraph = ckModelGraph;
    private string _sourcePath = sourcePath;
    private string _destinationPath = destinationPath;
    private CkModelId _ckModelId = ckModelId;
}