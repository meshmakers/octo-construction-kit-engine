using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

public interface ILinkHelpers
{
    string GetGeneratedFilePath(string docPath, CkModelId modelId, string extension);
    public string FormatAnchor(string unformattedAnchor);
    string GetCommonPathParts(CkModelId ckModelId);
    string CreateRelativeFilepath(string ckModelId, string suffix, string baseRelativePath);
}