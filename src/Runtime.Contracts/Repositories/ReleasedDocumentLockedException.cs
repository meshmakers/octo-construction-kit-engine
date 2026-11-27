using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     One write the released-document lock refused: the document, what the write would have done and which
///     attributes (or associations) it would have changed.
/// </summary>
/// <param name="DocumentId">The released document</param>
/// <param name="Operation">The refused operation: update, replace, insert, delete or association</param>
/// <param name="Changes">
///     Names of the attributes the write would have changed; for a delete <c>rtState</c>, for an association change
///     the association (<c>association '&lt;role&gt;' to '&lt;target&gt;'</c>)
/// </param>
/// <param name="State">The state value that makes the document released</param>
/// <param name="NumberRange">The well-known name of the number range whose release configuration locks it</param>
public sealed record ReleasedDocumentRefusal(
    RtEntityId DocumentId,
    string Operation,
    IReadOnlyList<string> Changes,
    string State,
    string NumberRange);

/// <summary>
///     A write that would change or delete a released document (design note
///     <c>docs/concept-number-ranges-and-released-documents.md</c>, D3, D8, D10). A document whose state value is one of
///     the <c>ReleasedValues</c> of its number range keeps its attributes, its number, its released state and its
///     outbound associations; only the <c>ChangeableAttributes</c> of the range may still change. The whole batch is
///     refused and nothing of it is written. The message of every refused document is in
///     <see cref="OperationResult" /> (engine message 168, <c>RtReleasedDocumentLocked</c>), the structured refusals in
///     <see cref="Refusals" />.
/// </summary>
[Serializable]
public sealed class ReleasedDocumentLockedException : PersistenceException
{
    /// <summary>
    ///     The stable, machine-readable code of the refusal. Every API answers it (GraphQL <c>extensions.code</c>, the
    ///     pipeline node's error) and the exception message starts with it, so a job status that only carries the
    ///     message carries the code too.
    /// </summary>
    public const string ErrorCode = "RELEASED_DOCUMENT_LOCKED";

    /// <summary>The engine message number of one refused document (<c>RtReleasedDocumentLocked</c>).</summary>
    public const int MessageNumber = 168;

    /// <summary>
    ///     Creates a new instance of <see cref="ReleasedDocumentLockedException" />.
    /// </summary>
    /// <param name="operationResult">One error message per refused document</param>
    /// <param name="refusals">The refused writes</param>
    public ReleasedDocumentLockedException(OperationResult operationResult,
        IReadOnlyList<ReleasedDocumentRefusal> refusals)
        : base($"{ErrorCode}: Released documents cannot be changed or deleted.{Environment.NewLine}{operationResult.GetMessages()}")
    {
        OperationResult = operationResult;
        Refusals = refusals;
    }

    /// <summary>One error message per refused document (engine message 168).</summary>
    public OperationResult OperationResult { get; }

    /// <summary>The refused writes, one per released document.</summary>
    public IReadOnlyList<ReleasedDocumentRefusal> Refusals { get; }
}
