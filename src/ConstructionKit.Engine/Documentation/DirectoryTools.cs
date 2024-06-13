using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal class DirectoryTools(ILogger<DirectoryTools> logger, ILinkHelpers linkHelpers) : IDirectoryTools
{
    public void BuildDirectory(string documentPath, CkModelId ckModelId)
    {
        var path = linkHelpers.GetCommonPathParts(ckModelId);
        path = Path.Combine(documentPath, path);

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
            logger.LogError("Error Creating Directory: {ex}", ex.ToString());
        }
    }
}