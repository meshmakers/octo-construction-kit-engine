using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

public interface IDirectoryTools
{
    void BuildDirectory(string docusaurusPath, CkModelId ckModelId);
    string GetRelativeDestinationDirectory(string directoryPath);
}