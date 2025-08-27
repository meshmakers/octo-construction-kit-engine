using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Exception thrown when a persistence error occurs.
/// </summary>
[Serializable]
public class PersistenceException : Exception
{
    /// <summary>
    ///     Creates a new instance of <see cref="PersistenceException" />.
    /// </summary>
    protected PersistenceException()
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="PersistenceException" />.
    /// </summary>
    protected PersistenceException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Creates a new instance of <see cref="PersistenceException" />.
    /// </summary>
    protected PersistenceException(string message, Exception inner) : base(message, inner)
    {
    }


    internal static Exception CkIdAttributeNotSet(Type type)
    {
        return new PersistenceException($"CkIdAttribute not set on type {type.FullName}");
    }

    internal static Exception CkTypeIdNotSet()
    {
        return new PersistenceException("CkTypeId not set");
    }
    
    internal static Exception CkTypeIdNotSet(Type type)
    {
        return new PersistenceException($"CkTypeId not set on type {type.FullName}");
    }

    internal static Exception AssociationRoleIdNotSet()
    {
        return new PersistenceException("AssociationRoleId not set");
    }

    internal static Exception RtIdNotSet()
    {
        throw new PersistenceException("RtId not set");
    }

    internal static Exception AttributeNameNotFound(string attributeName, CkTypeWithAttributesGraph ckTypeWithAttributesGraph)
    {
        return new PersistenceException(
            $"Attribute with name '{attributeName}' not found in type '{ckTypeWithAttributesGraph}'.");
    }

    internal static Exception CkEnumIdNotSet(string attributeName, CkTypeAttributeGraph ckTypeAttributeGraph)
    {
        return new PersistenceException(
            $"CkEnumId not set for attribute '{attributeName}' in type '{ckTypeAttributeGraph}'.");
    }

    internal static Exception CkEnumIdNotFound(string attributeName, CkTypeAttributeGraph ckTypeAttributeGraph)
    {
        return new PersistenceException(
            $"CkEnumId '{ckTypeAttributeGraph.ValueCkEnumId}' not found for attribute '{attributeName}' in type '{ckTypeAttributeGraph}'.");
    }

    internal static Exception EnumIdValueNotFound(CkId<CkEnumId> ckEnumId, int value)
    {
        return new PersistenceException(
            $"Value '{value}' not found in enum '{ckEnumId}'.");
    }
}