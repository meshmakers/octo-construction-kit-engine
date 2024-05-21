using Meshmakers.Octo.ConstructionKit.Contracts;
using Microsoft.Extensions.Logging;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools
{
    public class LinkHelpers(ILogger<LinkHelpers> logger) : ILinkHelpers
    {

        public string GetGeneratedFilePath(string docPath, CkModelId modelId, string extension)
        {
            return Path.Combine(BuildFilepath(docPath, modelId), $"{extension}.md");
        }

        public string FormatAnchor(string unformattedAnchor)
        {
            var anchor = unformattedAnchor.Replace(".", "").ToLower();
            return anchor;
        }

        public string GetCommonPathParts(CkModelId ckModelId)
        {
            try
            {
                var modelIdParts = ckModelId.SemanticVersionedFullName.Split(".");
                var path = "";

                foreach (var modelIdPart in modelIdParts)
                {
                    path = Path.Combine(path, modelIdPart);
                }
                return path;
            }
            catch (Exception e)
            {
                logger.LogError("Error Generating Path: {e}", e.ToString());
                throw;
            }
        }

        private string BuildFilepath(string docusaurusPath, CkModelId ckModelId)
        {
            var path = GetCommonPathParts(ckModelId);
            return Path.Combine(docusaurusPath, path);
        }

        private string CreateRelativeFilepath(CkModelId ckModelId, string suffix, string baseRelativePath /*= "/docs"*/)
        {
            var path = GetCommonPathParts(ckModelId);

            var linkWithBackslash = Path.Combine(baseRelativePath, path, suffix);
            
            return linkWithBackslash.Replace("\\", "/");
        }

        public string CreateRelativeFilepath(string ckModelId, string suffix, string baseRelativePath)
        {
            CkModelId modelId = new(ckModelId);
            return CreateRelativeFilepath(modelId, suffix, baseRelativePath);
        }


    }
}