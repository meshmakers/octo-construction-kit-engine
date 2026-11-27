using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Engine.Messages;
using Meshmakers.Octo.Runtime.Contracts;
using Meshmakers.Octo.Runtime.Contracts.Repositories;

namespace Meshmakers.Octo.Runtime.Engine.Repositories;

/// <summary>
///     A number a caller supplies for a document of a number range with a release configuration (design note D13). Such
///     a range hands out its numbers on release, so the number of its documents is the engine's: a write that supplies a
///     value other than the stored one is refused, as a <see cref="ReleasedDocumentLockedException" /> (code
///     <c>RELEASED_DOCUMENT_LOCKED</c>) with one engine message 174 per refused document. The number modifier refuses it
///     in the write funnel; the bulk import keeps the number from its file (D10).
/// </summary>
public static class ReleasedDocumentNumberRefusal
{
    /// <summary>The engine message number of one refused document (<c>RtReleasedDocumentNumberSupplied</c>).</summary>
    public const int MessageNumber = 174;

    /// <summary>One document whose write supplies a number.</summary>
    /// <param name="DocumentId">The document</param>
    /// <param name="Operation">The operation: insert, replace or update</param>
    /// <param name="AttributeName">The number attribute</param>
    /// <param name="Value">The supplied value</param>
    /// <param name="State">The state value the write leaves the document in (empty when it sets none)</param>
    /// <param name="NumberRange">The well-known name of the number range</param>
    public sealed record SuppliedNumber(
        RtEntityId DocumentId,
        string Operation,
        string AttributeName,
        object Value,
        string State,
        string NumberRange);

    /// <summary>The refusal of <paramref name="suppliedNumbers" /> (at least one).</summary>
    public static ReleasedDocumentLockedException Create(IReadOnlyList<SuppliedNumber> suppliedNumbers)
    {
        var result = new OperationResult();
        var refusals = new List<ReleasedDocumentRefusal>(suppliedNumbers.Count);
        foreach (var supplied in suppliedNumbers)
        {
            var ckTypeId = supplied.DocumentId.CkTypeId.FullName;
            refusals.Add(new ReleasedDocumentRefusal(supplied.DocumentId, supplied.Operation, [supplied.AttributeName],
                supplied.State, supplied.NumberRange));
            result.AddMessage(MessageCodes.RtReleasedDocumentNumberSupplied($"{ckTypeId}@{supplied.DocumentId.RtId}",
                supplied.Operation, supplied.DocumentId.RtId, ckTypeId, supplied.Value, supplied.AttributeName,
                supplied.NumberRange));
        }

        return new ReleasedDocumentLockedException(result, refusals);
    }
}
