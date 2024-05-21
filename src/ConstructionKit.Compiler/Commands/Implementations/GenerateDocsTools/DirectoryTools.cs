using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

public class DirectoryTools(ILogger<DirectoryTools> logger) : IDirectoryTools
{
    private readonly ILogger<DirectoryTools> _logger = logger;

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
            _logger.LogError("Error Creating Directory: {ex}", ex.ToString());
        }
    }

    public string GetRelativeDestinationDirectory(string directoryPath)
    {
        string directoryName;
        
        try
        { 
            directoryName = Path.GetFileName(directoryPath);
        }
        catch (ArgumentException e)
        {
            _logger.LogError("Invalid Characters in Path: {e}", e.ToString());
            throw;
        }
        return "/" + directoryName;
    }
}