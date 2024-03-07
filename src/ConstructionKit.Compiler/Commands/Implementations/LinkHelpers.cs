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

        public static async void LinkToType(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
        {
            await outputFile.WriteAsync($"link {ckTypeGraph.CkTypeId.GetName()} \"");
            await outputFile.WriteAsync(CreateRelativeFilepath(ckTypeGraph.CkTypeId.ModelId.FullName, "Types"));
            await outputFile.WriteLineAsync($"#{ckTypeGraph.CreateAnchor()}\"");
        }

        public static string CreateAnchor(this CkTypeGraph ckTypeGraph)
        {
            string ret = ckTypeGraph.CkTypeId.ModelId.ModelId;
            ret = ret.Replace("/", "-");
            ret = ret.Replace(".", "");
            ret = ret.ToLower();
            ret = ret + "-" + ckTypeGraph.CkTypeId.Key.TypeId.ToString().ToLower();

            return ret;

        }

        public static string GetCommonPathParts(CkModelId ckModelId)
        {
            string[] modelIdparts = ckModelId.ModelId.Split(".");
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

        public static string CreateRelativeFilepath(CkModelId ckModelId, string suffix)
        {
            string path = GetCommonPathParts(ckModelId);

            var linkWithBackslash = Path.Combine("/docs", path, suffix);

            return linkWithBackslash.Replace("\\", "/");
        }

        
    }
}