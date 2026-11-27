using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     A write that would change or delete an entry of the document audit trail (<c>System/DocumentAuditEntry</c>,
///     design note <c>docs/concept-number-ranges-and-released-documents.md</c>, D9). Audit entries are append-only: an
///     update, replace, delete, archive or an insert or import over a stored entry is refused for every caller, and the
///     whole batch is refused. One message per refused entry (engine message 173) is in <see cref="OperationResult" />.
/// </summary>
[Serializable]
public sealed class DocumentAuditEntryAppendOnlyException : PersistenceException
{
    /// <summary>
    ///     Creates a new instance of <see cref="DocumentAuditEntryAppendOnlyException" />.
    /// </summary>
    /// <param name="operationResult">One error message per refused audit entry</param>
    public DocumentAuditEntryAppendOnlyException(OperationResult operationResult)
        : base($"Document audit entries are append-only.{Environment.NewLine}{operationResult.GetMessages()}")
    {
        OperationResult = operationResult;
    }

    /// <summary>One error message per refused audit entry (engine message 173).</summary>
    public OperationResult OperationResult { get; }
}
