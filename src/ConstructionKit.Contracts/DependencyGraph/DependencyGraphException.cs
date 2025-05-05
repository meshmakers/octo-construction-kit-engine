namespace Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

/// <summary>
///    Used to indicate an exception in the DependencyGraph.
/// </summary>
public class DependencyGraphException : CkModelException
{
    private DependencyGraphException(string message) : base(message)
    {
    }

    internal static Exception AttributeNotFound(CkId<CkAttributeId> attributeCkAttributeId)
    {
        return new DependencyGraphException($"Attribute '{attributeCkAttributeId}' not found.");
    }

    internal static Exception CkTypeIdNotFound(CkId<CkTypeId> ckTypeId)
    {
        return new DependencyGraphException($"CkTypeId '{ckTypeId}' not found.");
    }

    internal static Exception CkRecordIdNotDefined(CkId<CkAttributeId> ckAttributeId)
    {
        return new DependencyGraphException($"CkRecordId not defined for attribute '{ckAttributeId}'.");
    }

    internal static Exception RecordNotFound(CkId<CkRecordId> ckRecordId)
    {
        return new DependencyGraphException($"Record '{ckRecordId}' not found.");
    }

    internal static Exception CkEnumIdNotDefined(CkId<CkAttributeId> ckAttributeId)
    {
        return new DependencyGraphException($"CkEnumId not defined for attribute '{ckAttributeId}'.");
    }
}