using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.Runtime.Contracts.RepositoryEntities;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Throws when an invalid attribute value is set.
/// </summary>
public class InvalidPathException : PersistenceException
{
    /// <summary>
    ///     Creates a new instance of <see cref="InvalidPathException" />.
    /// </summary>
    private InvalidPathException()
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="InvalidPathException" />.
    /// </summary>
    private InvalidPathException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="InvalidPathException" />.
    /// </summary>
    private InvalidPathException(string message, Exception inner) : base(message, inner)
    {
    }


    internal static Exception InvalidPathTerm(string path)
    {
        return new InvalidPathException($"Invalid path term '{path}'. Ensure that array indices are in square brackets and index is a number or wildcard.");
    }

    internal static Exception NoEmptyPaths()
    {
        return new InvalidPathException("Empty paths are not allowed.");
    }

    internal static Exception InvalidNavigationPropertyToken(RtTypeWithAttributes? rtTypeWithAttributes, PathTerm token)
    {
        return new InvalidPathException($"Invalid navigation property token '{token.Value}' for runtime type '{rtTypeWithAttributes}'.");
    }

    internal static Exception InvalidArrayIndexToken(RtTypeWithAttributes? rtTypeWithAttributes, PathTerm token)
    {
        return new InvalidPathException($"Invalid array index token '{token.Value}' for runtime type '{rtTypeWithAttributes}'.");
    }

    internal static Exception InvalidArrayIndex(RtTypeWithAttributes? rtTypeWithAttributes, PathTerm token)
    {
        return new InvalidPathException($"Invalid array index '{token.Value}' for runtime type '{rtTypeWithAttributes}'.");
    }

    internal static Exception InvalidArrayIndexData(RtTypeWithAttributes? rtTypeWithAttributes, PathTerm token)
    {
        return new InvalidPathException($"Array index does not point to records for token '{token.Value}' of runtime type '{rtTypeWithAttributes}'.");
    }

    internal static Exception MultipleNavigationEndsUnsupported(RtTypeWithAttributes? rtTypeWithAttributes, PathTerm token)
    {
        return new InvalidPathException($"Multiple navigation ends are not supported for token '{token.Value}' of runtime type '{rtTypeWithAttributes}'.");
    }

    internal static Exception PathNotSettable(RtTypeWithAttributes rtTypeWithAttributes, PathTerm? pathTupleTerm)
    {
        return new InvalidPathException($"Path '{pathTupleTerm?.Value}' is not settable for runtime type '{rtTypeWithAttributes}'.");
    }

    internal static Exception PathNotSettable(RtTypeWithAttributes rtTypeWithAttributes, List<PathTerm> tokens)
    {
        // Create a string representation of the path including value and type
        string path = string.Join(".", tokens.Select(p => $"{p.Value} ({p.Type})"));

        return new InvalidPathException($"Path '{path}' is not settable for runtime type '{rtTypeWithAttributes}'.");
    }

    internal static Exception CannotGetAttributeValue(RtTypeWithAttributes? rtTypeWithAttributes, PathTerm pathTupleTerm)
    {
        return new InvalidPathException($"Cannot get attribute value '{pathTupleTerm.Value}' for runtime type '{rtTypeWithAttributes}'.");
    }

    internal static Exception AttributeValueIsNotArray(RtTypeWithAttributes rtTypeWithAttributes, PathTerm pathTupleTerm)
    {
        return new InvalidPathException($"Attribute value '{pathTupleTerm.Value}' is not an array for runtime type '{rtTypeWithAttributes}'.");
    }

    internal static Exception InvalidTokenType(PathTerm token)
    {
        return new InvalidPathException($"Invalid token type '{token.Type}' for token '{token.Value}'.");
    }

    internal static Exception InvalidRuntimeType(RtTypeWithAttributes valueRtTypeWithAttribute, PathTerm token)
    {
        return new InvalidPathException($"Invalid runtime type '{valueRtTypeWithAttribute}' for token '{token.Value}'.");
    }

    internal static Exception InvalidPathTermTargetCkTypeIdMissing(IEnumerable<PathTerm> path, PathTerm currentToken)
    {
        return new InvalidPathException($"Invalid path term '{RtPathEvaluator.GetPath(path)}'. TargetCkTypeId is missing after '{currentToken.Value}'.");
    }

    internal static Exception CkTypeIdNotFound(string tenantId, CkId<CkTypeId> ckTypeId)
    {
        return new InvalidPathException($"CkTypeId '{ckTypeId}' not found for tenant '{tenantId}'.");
    }

    internal static Exception AssociationNotFound(IEnumerable<PathTerm> path, PathTerm navigationProperty, PathTerm targetTypeProperty)
    {
        return new InvalidPathException($"Association not found for path '{RtPathEvaluator.GetPath(path)}'. Navigation property '{navigationProperty.Value}' and target type property '{targetTypeProperty.Value}' do not match.");
    }

    internal static Exception NavigationPropertyNotFound(List<PathTerm> tokens, PathTerm token)
    {
        return new InvalidPathException($"Navigation property not found for token '{token.Value}' in path '{string.Join(".", tokens.Select(t => t.Value))}'.");
    }

    internal static Exception TargetCkTypeIdNotFound(List<PathTerm> tokens, PathTerm token)
    {
        return new InvalidPathException($"TargetCkTypeId not found for token '{token.Value}' in path '{string.Join(".", tokens.Select(t => t.Value))}'.");
    }

    internal static Exception PathNotSettableBecauseNull(List<PathTerm> tokens)
    {
        return new InvalidPathException($"Path '{string.Join(".", tokens.Select(t => t.Value))}' is not settable because it is null.");
    }

    internal static Exception PathNotSettable(PathTerm? pathTupleTerm, List<PathTerm> tokens)
    {
        return new InvalidPathException($"Path '{pathTupleTerm?.Value}' is not settable for runtime type '{string.Join(".", tokens.Select(t => t.Value))}'.");
    }
}