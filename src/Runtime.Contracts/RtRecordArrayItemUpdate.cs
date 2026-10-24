namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
///     Describes the atomic update of <b>one item</b> of a <c>RecordArray</c> attribute (AB#7445): the item whose
///     <see cref="MatchMember" /> equals <see cref="MatchValue" /> gets the <see cref="MemberUpdates" />, every other
///     item and every other member stays untouched.
/// </summary>
/// <remarks>
///     <para>
///         The update is executed by the persistence layer as ONE atomic statement (MongoDB: <c>$set</c> with
///         <c>arrayFilters</c> on the match member). The stored entity is not read, there is no version check and no
///         retry. Concurrent updates of different items therefore never interfere; concurrent updates of the same item
///         keep the last write.
///     </para>
///     <para>
///         Use it when several writers change <b>different items of the same RecordArray</b> of one entity in parallel
///         (for example one <c>DataPoint</c> per state in <c>Control.States</c>). A read-modify-write of the whole array
///         (read the entity, change one item in memory, write the array back) loses concurrent changes.
///     </para>
///     <para>
///         Names (<see cref="AttributeName" />, <see cref="MatchMember" />, the keys of <see cref="MemberUpdates" />) are
///         PascalCase as in <c>RtTypeWithAttributes.Attributes</c>. Members of value type <c>Secret</c> are refused, the
///         match member cannot be updated by the same item update, and only scalar members (String, Int, Int64, Double,
///         Boolean, DateTime, DateTimeOffset, TimeSpan, Enum) are supported.
///     </para>
/// </remarks>
public sealed class RtRecordArrayItemUpdate
{
    /// <summary>
    ///     Creates a new item update.
    /// </summary>
    /// <param name="attributeName">Name of the <c>RecordArray</c> attribute of the entity (PascalCase)</param>
    /// <param name="matchMember">Name of the record member that identifies the item (PascalCase)</param>
    /// <param name="matchValue">Value of <paramref name="matchMember" /> of the item to update; never <c>null</c></param>
    /// <param name="memberUpdates">Member name (PascalCase) to new value; at least one entry</param>
    [Newtonsoft.Json.JsonConstructor]
    [System.Text.Json.Serialization.JsonConstructor]
    public RtRecordArrayItemUpdate(string attributeName, string matchMember, object matchValue,
        IReadOnlyDictionary<string, object?> memberUpdates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        ArgumentException.ThrowIfNullOrWhiteSpace(matchMember);
        ArgumentNullException.ThrowIfNull(matchValue);
        ArgumentNullException.ThrowIfNull(memberUpdates);

        AttributeName = attributeName;
        MatchMember = matchMember;
        MatchValue = matchValue;
        MemberUpdates = new Dictionary<string, object?>(memberUpdates, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Name of the <c>RecordArray</c> attribute of the entity (PascalCase).
    /// </summary>
    public string AttributeName { get; }

    /// <summary>
    ///     Name of the record member that identifies the item (PascalCase), for example <c>ExternalId</c>.
    /// </summary>
    public string MatchMember { get; }

    /// <summary>
    ///     Value the <see cref="MatchMember" /> of the item must have.
    /// </summary>
    public object MatchValue { get; }

    /// <summary>
    ///     Member name (PascalCase) to new value. Only these members of the matched item are written.
    /// </summary>
    public IReadOnlyDictionary<string, object?> MemberUpdates { get; }
}
