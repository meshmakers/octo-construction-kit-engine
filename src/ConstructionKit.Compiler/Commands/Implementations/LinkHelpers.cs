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
            return Path.Combine(BuildFilepath(docPath, modelId), $"{modelId.SemanticVersionedFullName}-{extension}.md");
        }

        public static async void LinkToType(this CkTypeGraph ckTypeGraph, StreamWriter outputFile)
        {
            await outputFile.WriteAsync($"link {ckTypeGraph.CkTypeId.GetName()} \"");
            await outputFile.WriteAsync(CreateRelativeFilepath(ckTypeGraph.CkTypeId.ModelId.FullName));
            await outputFile.WriteLineAsync($"#{ckTypeGraph.CreateAnchor()}\"");
        }

        public static string CreateAnchor(this CkTypeGraph ckTypeGraph)
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
            Dictionary<string, IPathBuilder> pathBuilders = new()
            {
                {"System", new SystemPathBuilder() },
                { "Basic", new BasicPathBuilder() },
                { "IndustryBasic", new IndustryPathBuilder(new BasicPathBuilder()) },
                { "IndustryEnergy", new IndustryPathBuilder(new BasicPathBuilder()) },
                { "IndustryFluid", new IndustryPathBuilder(new BasicPathBuilder()) }
            };
            string modelIdPrefix = ckModelId.FullName[..ckModelId.FullName.IndexOf('-')];
            if (!pathBuilders.TryGetValue(modelIdPrefix, out IPathBuilder? value))
            {
                throw new ArgumentException($"Unsupported model ID prefix: {modelIdPrefix}");
            }
            return value.BuildPath(modelIdPrefix);
        }

        private static string BuildFilepath(string docusaurusPath, CkModelId ckModelId)
        {
            string path = GetCommonPathParts(ckModelId);
            return Path.Combine(docusaurusPath, path);
        }

        public static string CreateRelativeFilepath(CkModelId ckModelId)
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
            return Path.Combine("/docs", path, value.GetPathSuffix(modelIdPrefix));
        }

        
    }
}