using Meshmakers.Octo.ConstructionKit.Contracts;
using Meshmakers.Octo.ConstructionKit.Contracts.DependencyGraph;

namespace Meshmakers.Octo.Runtime.Contracts.Exchange;

/// <summary>
/// Represents an exception that occurs during exchange operations.
/// </summary>
public class ExchangeException : PersistenceException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExchangeException"/> class with a default message.
    /// </summary>
    public ExchangeException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExchangeException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">Message that describes the error.</param>
    public ExchangeException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExchangeException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">Message that describes the error.</param>
    /// <param name="inner">Inner exception that is the cause of this exception.</param>
    public ExchangeException(string message, Exception inner) : base(message, inner)
    {
    }

    /// <summary>
    /// The stable message number of the error, when the failure has one (for example 6384 for the
    /// blueprint-lock protection), otherwise null.
    /// </summary>
    public int? MessageNumber { get; private init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExchangeException"/> class with a message number.
    /// </summary>
    /// <param name="message">Message that describes the error.</param>
    /// <param name="messageNumber">Stable message number of the error.</param>
    public ExchangeException(string message, int messageNumber) : base(message)
    {
        MessageNumber = messageNumber;
    }

    /// <summary>
    /// AB#6392: a user-initiated import would overwrite entities that are locked by a blueprint (opted-in
    /// types, <c>ProtectBlueprintLocked</c>). Raised before anything is written; lists every offender.
    /// </summary>
    internal static Exception BlueprintLockedEntities(int messageNumber, IReadOnlyList<string> offenders)
    {
        return new ExchangeException(
            $"Import rejected [{messageNumber}]: {offenders.Count} entit" +
            (offenders.Count == 1 ? "y is" : "ies are") +
            " locked by blueprint and cannot be overwritten by users (access denied). Nothing was written. " +
            $"Locked: {string.Join("; ", offenders)}", messageNumber);
    }

    internal static Exception AttributeNotFound<TKey>(RtCkId<CkAttributeId> modelAttributeId, string elementType, CkId<TKey> ckId)
        where TKey : IComparable<TKey>, ICkElementId
    {
        return new ExchangeException($"Attribute '{modelAttributeId}' does not exist at {elementType} '{ckId}'.");
    }

    internal static Exception MandatoryAttributesMissing(int entityCount, string violationSummary)
    {
        return new ExchangeException(
            $"Import rejected (strict mandatory validation): {entityCount} entit" +
            (entityCount == 1 ? "y is" : "ies are") +
            " missing mandatory attributes that have no default value or auto-increment reference. " +
            $"Nothing was written. {violationSummary}");
    }

    internal static Exception CkEnumIdNotDefined(CkTypeAttributeGraph ckTypeAttributeGraph)
    {
        return new ExchangeException($"CkEnumId is not defined for attribute '{ckTypeAttributeGraph.AttributeName}'.");
    }

    internal static Exception CkEnumIdNotFound(CkTypeAttributeGraph typeAttributeGraph)
    {
        return new ExchangeException($"CkEnumId '{typeAttributeGraph.ValueCkEnumId}' not found.");
    }

    internal static Exception CkEnumWithOutOfRange(CkTypeAttributeGraph typeAttributeGraph, object value)
    {
        return new ExchangeException($"Value '{value}' is out of range for CkEnum '{typeAttributeGraph.ValueCkEnumId}'.");
    }

    internal static Exception BulkImportError(Exception innerException)
    {
        return new ExchangeException($"Bulk import failed: {innerException.Message}", innerException);
    }

    internal static Exception CkModelsMissing(string tenantId, ICollection<CkModelId> ckModelIds)
    {
        return new ExchangeException($"Models '{string.Join(", ", ckModelIds)}' are missing in tenant '{tenantId}'.");
    }

    internal static Exception CkModelsMissing(string tenantId, ICollection<CkModelIdVersionRange> ckModelIdRanges)
    {
        return new ExchangeException($"No models satisfying '{string.Join(", ", ckModelIdRanges)}' found in tenant '{tenantId}'.");
    }
}
