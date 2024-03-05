using Meshmakers.Octo.ConstructionKit.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations
{
    //Strategy Pattern
    public interface IPathBuilder
    {
        string BuildPath(CkModelId ckModelId);
    }

    public class SystemPathBuilder : IPathBuilder
    {
        public string BuildPath(CkModelId ckModelId)
        {
            return "System";
        }
    }

    public class BasicPathBuilder : IPathBuilder
    {
        public string BuildPath(CkModelId ckModelId)
        {
            return Path.Combine("System", "Basic");
        }
    }

    public class IndustryPathBuilder(IPathBuilder baseBuilder) : IPathBuilder
    {
        public readonly IPathBuilder _baseBuilder = baseBuilder;

        public string BuildPath(CkModelId ckModelId)
        {
            string basicPath = _baseBuilder.BuildPath(ckModelId);
            basicPath = Path.Combine(basicPath, "Industry");

            if (ckModelId.ModelId.Contains("IndustryEnergy"))
            {
                basicPath = Path.Combine(basicPath, "Energy");
            }
            else if (ckModelId.ModelId.Contains("IndustryFluid"))
            {
                basicPath = Path.Combine(basicPath, "Fluid");
            }
            return basicPath;

        }
    }
}
