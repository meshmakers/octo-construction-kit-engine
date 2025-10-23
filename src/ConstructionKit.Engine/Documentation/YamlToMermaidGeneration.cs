using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.Serialization;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Catalog;
using Meshmakers.Octo.ConstructionKit.Engine.Resolvers.Repository;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

/// <summary>
/// Generates a Mermaid Diagram in a Text File from given YAML CK model File
/// </summary>
public class YamlToMermaidGeneration
{
    private readonly ICatalogModelResolver _catalogModelResolver;
    private readonly ICkYamlSerializer _ckYamlSerializer;
    private readonly IMermaidGenerator _mermaidGenerator;

    /// <summary>
    /// DI For necessary Elements
    /// </summary>
    /// <param name="catalogModelResolver"></param>
    /// <param name="ckYamlSerializer"></param>
    /// <param name="mermaidGenerator"></param>
    public YamlToMermaidGeneration(ICatalogModelResolver catalogModelResolver, ICkYamlSerializer ckYamlSerializer, IMermaidGenerator mermaidGenerator)
    {
        _catalogModelResolver = catalogModelResolver;
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
#if NETSTANDARD2_0
            using var stream = File.OpenRead(filePath);
#else
            await using var stream = File.OpenRead(filePath);
#endif
        

        OperationResult operationResult = new(); // operation result is used to collect errors and warnings.
        var compiledModelRoot = await _ckYamlSerializer.DeserializeCompiledModelRootAsync(stream, filePath, operationResult).ConfigureAwait(false);

        // Resolves Dependencies
        var originFileResolver = new OriginFileResolver(filePath);
        var ckModelGraph = await _catalogModelResolver.HardResolveAsync(compiledModelRoot, originFileResolver, operationResult).ConfigureAwait(false);
        
        await _mermaidGenerator.GenerateMermaidDiagram(ckModelGraph, outputPath).ConfigureAwait(false);

    }
}