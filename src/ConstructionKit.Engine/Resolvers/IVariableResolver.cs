using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Resolvers;

/// <summary>
/// Resolves variables in the strings.
/// </summary>
public interface IVariableResolver
{
    /// <summary>
    /// Resolves the variables in the given value.
    /// </summary>
    /// <param name="value">Value that contains variables</param>
    /// <param name="location">Value that indicates the location of the value for logging purposes</param>
    /// <param name="operationResult">The operation result to log warnings and errors</param>
    /// <returns>The resolved value</returns>
    string Resolve(string value, string location, OperationResult operationResult);

    /// <summary>
    /// Sets a variable to the resolver.
    /// </summary>
    /// <param name="variableName">Name of the variable</param>
    /// <param name="variableValue">Value of the variable</param>
    void SetVariable(string variableName, string variableValue);
}