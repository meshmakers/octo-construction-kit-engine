using System.Runtime.Serialization;

namespace Meshmakers.Octo.SystematizedData.Persistence.DataAccess;

[Serializable]
public class OperationFailedException : PersistenceException
{
    //
    // For guidelines regarding the creation of new exception types, see
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
    // and
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
    //

    public OperationFailedException()
    {
    }

    public OperationFailedException(string message) : base(message)
    {
    }

    public OperationFailedException(string message, Exception inner) : base(message, inner)
    {
    }

    protected OperationFailedException(
        SerializationInfo info,
        StreamingContext context) : base(info, context)
    {
    }

    public static Exception CreateWithMessage(string text)
    {
        return new OperationFailedException(text);
    }

    public static Exception FormulaCalculationFailed(object searchTerm)
    {
        return new OperationFailedException($"Formula '{searchTerm}' cannot be calculated.");
    }

    public static Exception FormulaEvaluationFailed(object searchTerm)
    {
        return new OperationFailedException($"Term '{searchTerm}' cannot be evaluated by formula.");
    }

    public static Exception AttributeNameResolutionFailed(string fieldFilterAttributeName)
    {
        return new OperationFailedException($"Attribute name '{fieldFilterAttributeName}' cannot be resolved.");
    }

    public static Exception AttributeDoesNotExist(string attributeName, string getEntityName)
    {
        return new OperationFailedException(
            $"Attribute '{attributeName}' does not exist on type '{getEntityName}'");
    }

    public static Exception NoFilterSet()
    {
        return new OperationFailedException("No filter set.");
    }

    public static Exception ValidationErrors()
    {
        return new OperationFailedException("Validation errors.");
    }

    public static Exception BulkImportError()
    {
        return new OperationFailedException("Bulk import error.");
    }
}
