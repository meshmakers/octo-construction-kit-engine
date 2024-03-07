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
        public virtual string GetPathSuffix(string modelIdPrefix, string suffix)
        {
            throw new NotImplementedException($"Path suffix generation not implemented for prefix: {modelIdPrefix}");
        }
    }

    public class SystemModelIdPrefixHandler : ModelIdPrefixHandler
    {
        public override string GetPathSuffix(string modelIdPrefix, string suffix)
        {
            if (modelIdPrefix.Equals("System"))
            {
                return $"{suffix}";
            }
            else
            {
                //Potential To Remove System from Final Prefix
                return $"{modelIdPrefix[6..]}-{suffix}";
            }
        }
    }

    public class BasicModelIdPrefixHandler : ModelIdPrefixHandler
    {
        public override string GetPathSuffix(string modelIdPrefix, string suffix)
        {
            if (modelIdPrefix.Contains("IndustryBasic"))
            {
                return $"{suffix}";
            }
            else
            {
                return $"{suffix}";
            }
        }
    }

    public class IndustryModelIdPrefixHandler : ModelIdPrefixHandler
    {
        public override string GetPathSuffix(string modelIdPrefix, string suffix)
        {
            //modelIdPrefix[8..] to remove Industry Prefix if desired
            return $"{suffix}";
        }
    }
}
