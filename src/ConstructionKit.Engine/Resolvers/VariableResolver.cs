using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

internal class VariableResolver : IVariableResolver
{
    private const string Pattern = @"\${\s*([\w\d\._]+)\s*}";
    private readonly Dictionary<string, string> _variables = new();

    public string Resolve(string value, string location, OperationResult operationResult)
    {
        string result = Regex.Replace(value, Pattern, m =>
        {
            string varName = m.Groups[1].Value;
            if (_variables.TryGetValue(varName, out string? replacementValue))
            {
                return replacementValue;
            }

            operationResult.AddMessage(MessageCodes.VariableUnknown(location, varName));

            return m.Value; // No replacement found, return the original match
        });
        return result;
    }

    public void SetVariable(string variableName, string variableValue)
    {
        _variables[variableName] = variableValue;
    }
}