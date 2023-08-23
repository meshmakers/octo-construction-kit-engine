namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
/// Used to indicate an exception in the CkModel.
/// </summary>
public class CkModelException : Exception
{
    public CkModelException()
    {
    }

    public CkModelException(string message) : base(message)
    {
    }

    public CkModelException(string message, Exception inner) : base(message, inner)
    {
    }
}