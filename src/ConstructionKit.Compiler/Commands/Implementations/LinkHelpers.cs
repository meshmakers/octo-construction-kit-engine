using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;
using System.IO;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations
{
    internal static class LinkHelpers
    {

        public static string GetGeneratedFilePath(string docPath, CkModelId modelId, string extension)
        {
            //return Path.Combine(BuildFilepath(docPath, modelId), $"{modelId.SemanticVersionedFullName}-{extension}.md");
            return Path.Combine(BuildFilepath(docPath, modelId), $"{extension}.md");
        }

        public static string FormatAnchor(string unformattedAnchor)
        {
            string anchor = unformattedAnchor.Replace(".", "").ToLower();
            return anchor;
        }

        public static string GetCommonPathParts(CkModelId ckModelId)
        {
            //string[] modelIdparts = ckModelId.ModelId.Split(".");
            string[] modelIdparts = ckModelId.SemanticVersionedFullName.Split(".");
            string path = "";

            for (int i = 0; i < modelIdparts.Length; i++)
            {
                path = Path.Combine(path, modelIdparts[i]);
            }

            return path;
        }

        private static string BuildFilepath(string docusaurusPath, CkModelId ckModelId)
        {
            string path = GetCommonPathParts(ckModelId);
            return Path.Combine(docusaurusPath, path);
        }

        public static string CreateRelativeFilepath(CkModelId ckModelId, string suffix, string baseRelativePath /*= "/docs"*/)
        {
            string path = GetCommonPathParts(ckModelId);

            var linkWithBackslash = Path.Combine(baseRelativePath, path, suffix);
            
            return linkWithBackslash.Replace("\\", "/");
        }

        public static string CreateRelativeFilepath(string ckModelId, string suffix, string baseRelativePath)
        {
            CkModelId modelId = new(ckModelId);
            return CreateRelativeFilepath(modelId, suffix, baseRelativePath);
        }


    }
}