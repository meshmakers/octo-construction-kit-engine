namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Used to indicate an exception in the CkModel.
/// </summary>
public class CkModelException : Exception
{
    /// <inheritdoc />
    protected CkModelException()
    {
    }

    /// <inheritdoc />
    protected CkModelException(string message) : base(message)
    {
    }

    /// <inheritdoc />
    protected CkModelException(string message, Exception inner) : base(message, inner)
    {
    }
}