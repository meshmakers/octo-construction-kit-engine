using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

/// <summary>
/// Generates a Mermaid Diagram in a Text File from given YAML CK model File
/// </summary>
public class YamlToMermaidGeneration
{
    private readonly IModelResolver _modelResolver;
    private readonly ICkYamlSerializer _ckYamlSerializer;
    private readonly IMermaidGenerator _mermaidGenerator;

    /// <summary>
    /// DI For necessary Elements
    /// </summary>
    /// <param name="modelResolver"></param>
    /// <param name="ckYamlSerializer"></param>
    /// <param name="mermaidGenerator"></param>
    public YamlToMermaidGeneration(IModelResolver modelResolver, ICkYamlSerializer ckYamlSerializer, IMermaidGenerator mermaidGenerator)
    {
        _modelResolver = modelResolver;
        _ckYamlSerializer = ckYamlSerializer;
        _mermaidGenerator = mermaidGenerator;
    }

    /// <summary>
    /// Generates a Mermaid Diagram in a Text File from given YAML CK model File
    /// </summary>
    /// <param name="filePath">Path of YAML CK model File</param>
    /// <param name="outputPath">Path where File will be saved, include extension e.g. "diagram.txt"</param>
    public async Task YamlToMermaid(string filePath, string outputPath)
        {
        using var stream = File.OpenRead(filePath);

        OperationResult operationResult = new(); // operation result is used to collect errors and warnings.
        var compiledModelRoot = await _ckYamlSerializer.DeserializeCompiledModelRootAsync(stream, filePath, operationResult).ConfigureAwait(false);

        // Resolves Dependencies
        var originFileResolver = new OriginFileResolver(filePath);
        var resolvedTypes = await _modelResolver.ResolveAsync(compiledModelRoot, originFileResolver, operationResult).ConfigureAwait(false);
        
        await _mermaidGenerator.GenerateMermaidDiagram(resolvedTypes, outputPath).ConfigureAwait(false);

    }
}