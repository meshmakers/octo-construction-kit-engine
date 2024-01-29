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
    /// <returns>The resolved value</returns>
    string Resolve(string value);

    /// <summary>
    /// Sets a variable to the resolver.
    /// </summary>
    /// <param name="variableName">Name of the variable</param>
    /// <param name="variableValue">Value of the variable</param>
    void SetVariable(string variableName, string variableValue);
}