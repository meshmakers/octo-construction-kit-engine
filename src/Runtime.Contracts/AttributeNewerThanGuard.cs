namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Optimistic-concurrency guard for conditional updates: the write applies only if the
///     current persisted value at <see cref="AttributePath" /> is missing, null, or less than
///     or equal to <see cref="NewValue" />. Prevents older state from overwriting newer state
///     when the order of arriving writes does not reflect the order in which they were
///     generated — e.g. a late commit from a controller pod that is mid-shutdown racing with
///     the write from the replacement pod that has already taken over.
/// </summary>
/// <param name="AttributePath">
///     Storage-layer dot-notation path to the timestamp field that gates the update.
///     Examples: <c>"attributes.communicationStateTimestamp"</c> for a CK attribute,
///     <c>"rtChangedDateTime"</c> for an entity-level field.
/// </param>
/// <param name="NewValue">
///     The timestamp the caller is about to write. The update only applies if the
///     persisted value at <see cref="AttributePath" /> is &lt;= this.
/// </param>
public sealed record AttributeNewerThanGuard(string AttributePath, DateTime NewValue);
