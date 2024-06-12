using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

/// <summary>
/// Help build links to different Items
/// </summary>
public interface ILinkHelpers
{
    /// <summary>
    /// Gets a Generated Filepath from the path of the document, the modelID and the desired extension 
    /// </summary>
    /// <param name="docPath">Base Path for generation</param>
    /// <param name="modelId">CkModelID</param>
    /// <param name="extension">Name of the file and type ex. "test.txt"</param>
    /// <returns></returns>
    string GetGeneratedFilePath(string docPath, CkModelId modelId, string extension);
    
    /// <summary>
    /// Formats the anchor to suit the style requirements
    /// </summary>
    /// <param name="unformattedAnchor">an unformatted anchor element</param>
    /// <returns></returns>
    public string FormatAnchor(string unformattedAnchor);
    
    /// <summary>
    /// Helper function that gets the common path parts of a given modelID
    /// </summary>
    /// <param name="ckModelId">CkModelID</param>
    /// <returns></returns>
    string GetCommonPathParts(CkModelId ckModelId);
    
    /// <summary>
    /// Creates a relative filepath from given modelId, suffix and relative base path
    /// </summary>
    /// <param name="ckModelId">CkModelID</param>
    /// <param name="suffix">Example "Types"</param>
    /// <param name="baseRelativePath">Base path of given model in relative form ex. /Basic</param>
    /// <returns></returns>
    string CreateRelativeFilepath(string ckModelId, string suffix, string baseRelativePath);
}