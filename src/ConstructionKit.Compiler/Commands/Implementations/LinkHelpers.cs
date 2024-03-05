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

        private static string BuildFilepath(string docusaurusPath, CkModelId ckModelId)
        {
            string path = "System";
            path = Path.Combine(docusaurusPath, path);

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

        private static string CreateRelativeFilepath(CkModelId ckModelId)
        {
            string path = "\\docs\\System";

            if (ckModelId.ModelId.Contains("Basic"))
            {
                path = Path.Combine(path, "Basic");

                if (ckModelId.ModelId.Contains("IndustryBasic"))
                {
                    path = Path.Combine(path, "Industry");
                    path = Path.Combine(path, "IndustryBasic-Types");
                }
                else
                {
                    path = Path.Combine(path, "Basic-Types");
                }
            }
            else if (ckModelId.ModelId.Contains("IndustryEnergy"))
            {
                path = Path.Combine(path, "Basic", "Industry", "Energy");
                path = Path.Combine(path, "IndustryEnergy-Types");
            }
            else if (ckModelId.ModelId.Contains("IndustryFluid"))
            {
                path = Path.Combine(path, "Basic", "Industry", "Fluid");
                path = Path.Combine(path, "IndustryFluid-Types");
            }
            else
            {
                path = Path.Combine(path, "System-Types");
            }

            return path;
        }
    }
}