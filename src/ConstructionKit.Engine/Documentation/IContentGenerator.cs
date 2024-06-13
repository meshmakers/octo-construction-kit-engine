using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

/// <summary>
/// Generates All .md Documentation Content of a given Model Graph
/// </summary>
public interface IContentGenerator
{
    /// <summary>
    /// Writes a Markdown Table to File that contains all Attributes present in the modelGraph
    /// </summary>
    /// <param name="modelGraph">The Resolved CK Model Graph</param>
    /// <param name="documentPath">Path where the Table is Saved</param>
    /// <param name="ckModelId">Used to determine the Files Position in the File tree</param>
    /// <param name="versionNumber">Version of the Model used</param>
    /// <param name="directoryPath"></param>
    /// <returns></returns>
    Task GenerateAttributesMarkdownTable(CkModelGraph modelGraph, string 
        documentPath, CkModelId ckModelId, string? versionNumber, string directoryPath);

    /// <summary>
    /// Writes a Markdown Table to File that contains all Enums present in the modelGraph
    /// </summary>
    /// <param name="modelGraph">The Resolved CK Model Graph</param>
    /// <param name="documentPath">Path where the Table is Saved</param>
    /// <param name="ckModelId">Used to determine the Files Position in the File tree</param>
    /// <param name="versionNumber">Version of the Model used</param>
    /// <param name="directoryPath"></param>
    /// <returns></returns>
    Task GenerateEnumsMarkdownTable(CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId, string? versionNumber, string directoryPath);

    /// <summary>
    /// Writes a Markdown Table to File that contains all Records present in the modelGraph
    /// </summary>
    /// <param name="modelGraph">The Resolved CK Model Graph</param>
    /// <param name="documentPath">Path where the Table is Saved</param>
    /// <param name="ckModelId">Used to determine the Files Position in the File tree</param>
    /// <param name="versionNumber">Version of the Model used</param>
    /// <param name="directoryPath"></param>
    /// <returns></returns>
    Task GenerateRecordsMarkdownTable(CkModelGraph modelGraph, string documentPath, 
        CkModelId ckModelId, string? versionNumber, string directoryPath);

    /// <summary>
    /// Writes a Markdown Table to File that contains all Types present in the modelGraph
    /// </summary>
    /// <param name="modelGraph">The Resolved CK Model Graph</param>
    /// <param name="documentPath">Path where the Table is Saved</param>
    /// <param name="ckModelId">Used to determine the Files Position in the File tree</param>
    /// <param name="versionNumber">Version of the Model used</param>
    /// <param name="directoryPath"></param>
    /// <returns></returns>
    Task GenerateTypesMarkdownTable(CkModelGraph modelGraph, 
        string documentPath, CkModelId ckModelId, string? versionNumber, string directoryPath);

    /// <summary>
    /// Writes a Markdown Table to File that contains all Association Roles present in the modelGraph
    /// </summary>
    /// <param name="modelGraph">The Resolved CK Model Graph</param>
    /// <param name="documentPath">Path where the Table is Saved</param>
    /// <param name="ckModelId">Used to determine the Files Position in the File tree</param>
    /// <param name="versionNumber">Version of the Model used</param>
    /// <param name="directoryPath"></param>
    /// <returns></returns>
    Task GenerateAssociationRolesMarkdownTable(CkModelGraph modelGraph,
        string documentPath, CkModelId ckModelId, string? versionNumber, string directoryPath);

    /// <summary>
    /// Writes a Markdown Table to File that contains a version history of the given ckModelId
    /// </summary>
    /// <param name="docPath">Path where the Table is Saved</param>
    /// <param name="ckModelId">Used to determine the Files Position in the File tree</param>
    /// <param name="versionNumber">Version of the Model used</param>
    /// <param name="directoryPath"></param>
    /// <returns></returns>
    Task GenerateVersionHistory(string docPath, CkModelId ckModelId, string? versionNumber, string directoryPath);
}