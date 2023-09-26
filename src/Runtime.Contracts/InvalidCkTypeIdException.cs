using System;
using System.Runtime.Serialization;

namespace Meshmakers.Octo.SystematizedData.Persistence.DataAccess;

[Serializable]
public class InvalidCkTypeIdException : OperationFailedException
{
    //
    // For guidelines regarding the creation of new exception types, see
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
    // and
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
    //

    public InvalidCkTypeIdException()
    {
    }

    public InvalidCkTypeIdException(string message) : base(message)
    {
    }

    public InvalidCkTypeIdException(string message, Exception inner) : base(message, inner)
    {
    }

    protected InvalidCkTypeIdException(
        SerializationInfo info,
        StreamingContext context) : base(info, context)
    {
    }
}
