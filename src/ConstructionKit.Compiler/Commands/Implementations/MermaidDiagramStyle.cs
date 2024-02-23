using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations
{
    public class MermaidDiagramStyle(string fill)
    {
        string _fill = fill;
        public string GetFill()
        {
            return _fill;
        }
    }
}
