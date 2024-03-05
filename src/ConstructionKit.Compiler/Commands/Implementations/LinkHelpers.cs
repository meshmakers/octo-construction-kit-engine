using Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations
{
    internal static class LinkHelpers
    {

        public static string GetGeneratedFilePath(string docPath, CkModelId modelId, string extension)
        {
            return Path.Combine(BuildFilepath(docPath, modelId), $"{modelId.SemanticVersionedFullName}-{extension}.md");
        }

        public static async void LinkToType(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
        {
            await outputFile.WriteAsync($"link {ckTypeGraph.CkTypeId.GetName()} \"");
            await outputFile.WriteAsync(CreateRelativeFilepath(ckTypeGraph.CkTypeId.ModelId.FullName));
            await outputFile.WriteLineAsync($"#{ckTypeGraph.CreateAnchor()}\"");
        }

        private static string CreateAnchor(this CkTypeGraph ckTypeGraph)
        {
            string ret = ckTypeGraph.CkTypeId.FullName;
            ret = ret.Replace("/", "-");
            ret = ret.Replace(".", "");
            ret = ret.ToLower();

            //Remove Last Version Number
            int LastHyphen = ret.LastIndexOf('-');

            if (LastHyphen == -1)
            {
                throw new Exception();
            }
            else
            {
                string res = ret[..LastHyphen];
                return res;
            }

        }

        private static string GetCommonPathParts(CkModelId ckModelId)
        {
            string path = "System";
            if (ckModelId.ModelId.Contains("Basic"))
            {
                path = Path.Combine(path, "Basic");
                if (ckModelId.ModelId.Contains("IndustryBasic"))
                {
                    path = Path.Combine(path, "Industry");
                }
            }
            else if (ckModelId.ModelId.Contains("IndustryEnergy"))
            {
                path = Path.Combine(path, "Basic", "Industry", "Energy");
            }
            else if (ckModelId.ModelId.Contains("IndustryFluid"))
            {
                path = Path.Combine(path, "Basic", "Industry", "Fluid");
            }
            return path;
        }

        private static string BuildFilepath(string docusaurusPath, CkModelId ckModelId)
        {
            string path = GetCommonPathParts(ckModelId);
            return Path.Combine(docusaurusPath, path);
        }

        private static string CreateRelativeFilepath(CkModelId ckModelId)
        {
            string path = GetCommonPathParts(ckModelId);

            //Strategy Pattern
            Dictionary<string, ModelIdPrefixHandler> prefixHandlers = new()
            {
                { "System", new SystemModelIdPrefixHandler() },
                { "Basic", new BasicModelIdPrefixHandler() },
                { "IndustryBasic", new BasicModelIdPrefixHandler() },
                { "IndustryEnergy", new IndustryModelIdPrefixHandler() },
                { "IndustryFluid", new IndustryModelIdPrefixHandler() }
            };
            string modelIdPrefix = ckModelId.FullName[..ckModelId.FullName.IndexOf('-')];
            if (!prefixHandlers.TryGetValue(modelIdPrefix, out ModelIdPrefixHandler? value))
            {
                throw new ArgumentException($"Unsupported model ID prefix: {modelIdPrefix}");
            }
            return Path.Combine("\\docs", path, value.GetPathSuffix(modelIdPrefix));
        }

        
    }
}