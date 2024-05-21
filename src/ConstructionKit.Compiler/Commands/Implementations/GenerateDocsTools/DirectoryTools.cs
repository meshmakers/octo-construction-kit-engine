using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

public class DirectoryTools(ILogger<GenerateDocsCommand> logger)
{
    private readonly ILogger<GenerateDocsCommand> _logger = logger;

    public void BuildDirectory(string docusaurusPath, CkModelId ckModelId)
    {
        string path = new(LinkHelpers.GetCommonPathParts(ckModelId));
        path = Path.Combine(docusaurusPath, path);

        try
        {
            if (Directory.Exists(path))
            {
                return;
            }

            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error Creating Directory {ex}", ex);
        }
    }

    public string GetRelativeDestinationDirectory(string directoryPath)
    {
        return "/" + Path.GetFileName(directoryPath);
    }
}