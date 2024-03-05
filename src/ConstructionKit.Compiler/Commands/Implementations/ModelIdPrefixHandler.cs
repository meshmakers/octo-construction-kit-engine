using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations
{
    //Strategy Pattern
    public class ModelIdPrefixHandler
    {
        public virtual string GetPathSuffix(string modelIdPrefix)
        {
            throw new NotImplementedException($"Path suffix generation not implemented for prefix: {modelIdPrefix}");
        }
    }

    public class SystemModelIdPrefixHandler : ModelIdPrefixHandler
    {
        public override string GetPathSuffix(string modelIdPrefix)
        {
            if (modelIdPrefix.Equals("System"))
            {
                return $"{modelIdPrefix}-Types";
            }
            else
            {
                //Potential To Remove System from Final Prefix
                return $"{modelIdPrefix[6..]}-Types";
            }
        }
    }

    public class BasicModelIdPrefixHandler : ModelIdPrefixHandler
    {
        public override string GetPathSuffix(string modelIdPrefix)
        {
            if (modelIdPrefix.Contains("IndustryBasic"))
            {
                return "IndustryBasic-Types";
            }
            else
            {
                return "Basic-Types";
            }
        }
    }

    public class IndustryModelIdPrefixHandler : ModelIdPrefixHandler
    {
        public override string GetPathSuffix(string modelIdPrefix)
        {
            //modelIdPrefix[8..] to remove Industry Prefix if desired
            return $"{modelIdPrefix}-Types";
        }
    }
}
