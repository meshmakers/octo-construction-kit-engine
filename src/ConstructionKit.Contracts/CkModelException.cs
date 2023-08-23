namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Used to indicate an exception in the CkModel.
/// </summary>
public class CkModelException : Exception
{
    /// <inheritdoc />
    public CkModelException()
    {
    }

    /// <inheritdoc />
    public CkModelException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public CkModelException(string message, Exception inner) : base(message, inner)
    {
    }
}