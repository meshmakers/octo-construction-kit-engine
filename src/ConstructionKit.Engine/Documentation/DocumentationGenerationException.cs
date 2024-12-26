using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

/// <summary>
/// General Exception for Documentation Generation
/// </summary>
public class DocumentationGenerationException : Exception
{
    /// <inheritdoc />
    public DocumentationGenerationException()
    {
    }

    /// <inheritdoc />
    public DocumentationGenerationException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public DocumentationGenerationException(string message, Exception inner) : base(message, inner)
    {
    }

    internal static Exception AssociationRoleNotFound(CkId<CkAssociationRoleId> associationCkRoleId)
    {
        return new DocumentationGenerationException($"Association Role with Id {associationCkRoleId} not found");
    }
}