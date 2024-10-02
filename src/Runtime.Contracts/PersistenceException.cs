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
}