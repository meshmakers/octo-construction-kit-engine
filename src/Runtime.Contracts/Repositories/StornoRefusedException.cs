using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Contracts.Repositories;

/// <summary>
///     A storno (association role <c>System/Cancels</c>, storno to original) the engine refuses (design note
///     <c>docs/concept-number-ranges-and-released-documents.md</c>, D6, D7): a storno of a document that is not
///     released (169), a second released storno of the same original (170), a storno of a storno (171) or a storno of
///     another type than the one the original's number range configures (172). The whole batch is refused and nothing
///     of it is written; one message per refused storno is in <see cref="OperationResult" />.
/// </summary>
[Serializable]
public sealed class StornoRefusedException : PersistenceException
{
    /// <summary>
    ///     Creates a new instance of <see cref="StornoRefusedException" />.
    /// </summary>
    /// <param name="operationResult">One error message per refused storno</param>
    public StornoRefusedException(OperationResult operationResult)
        : base($"Storno refused.{Environment.NewLine}{operationResult.GetMessages()}")
    {
        OperationResult = operationResult;
    }

    /// <summary>One error message per refused storno (engine messages 169 to 172).</summary>
    public OperationResult OperationResult { get; }
}
