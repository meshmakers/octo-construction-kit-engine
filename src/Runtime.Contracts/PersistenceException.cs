namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Exception thrown when a persistence error occurs.
/// </summary>
[Serializable]
public class PersistenceException : Exception
{
    /// <summary>
    /// Creates a new instance of <see cref="PersistenceException"/>.
    /// </summary>
    protected PersistenceException()
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="PersistenceException"/>.
    /// </summary>
    protected PersistenceException(string message) : base(message)
    {
    }

    /// <summary>
    /// Creates a new instance of <see cref="PersistenceException"/>.
    /// </summary>
    protected PersistenceException(string message, Exception inner) : base(message, inner)
    {
    }


    internal static Exception CkIdAttributeNotSet(Type type)
    {
        throw new PersistenceException($"CkIdAttribute not set on type {type.FullName}");
    }
}
