using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts;

/// <summary>
///     Shared conventions of the <see cref="AttributeValueTypesDto.Secret" /> value type (AB#5528,
///     <c>docs/concept-secret-attribute-type.md</c>). One place for the rules that the compiler, the
///     blueprint seed lint and the runtime write path (AB#5532) must agree on.
/// </summary>
public static class SecretAttributeConventions
{
    /// <summary>
    ///     Name of the system construction kit model.
    /// </summary>
    public const string SystemModelName = "System";

    /// <summary>
    ///     The first System model version whose engine understands the Secret value type
    ///     (decision 12). A model that uses Secret must depend on <c>System &gt;= 2.5</c>, so an
    ///     engine that does not know the value type fails with a dependency error instead of
    ///     misreading the attribute.
    /// </summary>
    public static readonly CkVersion MinimumSystemVersion = new("2.5.0");

    /// <summary>
    ///     Value types a record key (<see cref="CkRecordDto.RecordKey" />) may have: scalar values
    ///     that identify an element and compare by value.
    /// </summary>
    public static readonly IReadOnlyCollection<AttributeValueTypesDto> AllowedRecordKeyValueTypes =
    [
        AttributeValueTypesDto.String,
        AttributeValueTypesDto.Int,
        AttributeValueTypesDto.Int64,
        AttributeValueTypesDto.Enum
    ];

    /// <summary>
    ///     Prefix of the <c>TODO_SET_&lt;UPPER_SNAKE&gt;</c> legacy placeholder form (see
    ///     <see cref="IsTodoSetPlaceholder" />).
    /// </summary>
    public const string TodoSetPlaceholderPrefix = "TODO_SET_";

    /// <summary>
    ///     MIGRATION ONLY (decisions 2026-10-06, item 1): true when a LEGACY value - a string found in a
    ///     Secret slot in storage, written before the slot became a Secret - is exactly a placeholder in one
    ///     of the two forms that blueprints and apps used before the Secret value type:
    ///     <list type="bullet">
    ///         <item>
    ///             a non-empty text enclosed in exactly one pair of angle brackets such as
    ///             <c>&lt;set-after-install&gt;</c> (<see cref="IsAngleBracketPlaceholder" />);
    ///         </item>
    ///         <item>
    ///             <c>TODO_SET_&lt;UPPER_SNAKE&gt;</c> such as <c>TODO_SET_CLIENT_SECRET</c>
    ///             (<see cref="IsTodoSetPlaceholder" />).
    ///         </item>
    ///     </list>
    ///     Leading and trailing whitespace is ignored. Such a legacy value is converted once to "not set"
    ///     (the encrypt sweep, the CK migration hook, and the write step when it meets a stored legacy
    ///     string, e.g. on carry-over) and counted as a normalised placeholder.
    ///     <para>
    ///         Placeholders have NO meaning anywhere else: a <c>&lt;...&gt;</c> or <c>TODO_SET_...</c> string
    ///         sent through any API is an ordinary value and is encrypted like any other, and blueprint seeds
    ///         may not carry one in a Secret slot (<see cref="IsAllowedSeedValue" />). Never call this for
    ///         input values.
    ///     </para>
    /// </summary>
    /// <param name="value">The legacy stored text</param>
    /// <returns>True for a legacy placeholder</returns>
    public static bool IsLegacyPlaceholder(string? value)
    {
        return IsAngleBracketPlaceholder(value) || IsTodoSetPlaceholder(value);
    }

    /// <summary>
    ///     True when <paramref name="value" /> is a non-empty text enclosed in exactly one pair of angle
    ///     brackets, e.g. <c>&lt;set-after-install&gt;</c>. <c>&lt;&gt;</c>, <c>&lt;a&gt;&lt;b&gt;</c> and
    ///     <c>&lt;&lt;x&gt;&gt;</c> are not placeholders.
    /// </summary>
    /// <param name="value">The value to check</param>
    /// <remarks>Migration only, see <see cref="IsLegacyPlaceholder" />.</remarks>
    /// <returns>True for an angle-bracket placeholder</returns>
    public static bool IsAngleBracketPlaceholder(string? value)
    {
        if (value == null)
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 3 || trimmed[0] != '<' || trimmed[trimmed.Length - 1] != '>')
        {
            return false;
        }

        // Exactly one bracket pair - "<a><b>" or "<<x>>" is not a placeholder but a value that
        // happens to contain brackets.
        for (var i = 1; i < trimmed.Length - 1; i++)
        {
            if (trimmed[i] == '<' || trimmed[i] == '>')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     True when <paramref name="value" /> is <c>TODO_SET_</c> followed by one or more
    ///     upper-case snake-case words: <c>[A-Z0-9]+(_[A-Z0-9]+)*</c>, e.g. <c>TODO_SET_PASSWORD</c>
    ///     or <c>TODO_SET_AZURE_TENANT_ID</c>. The bare prefix, lower-case letters, a trailing or
    ///     doubled underscore and any other character make it a regular value.
    /// </summary>
    /// <param name="value">The value to check</param>
    /// <remarks>Migration only, see <see cref="IsLegacyPlaceholder" />.</remarks>
    /// <returns>True for a <c>TODO_SET_</c> placeholder</returns>
    public static bool IsTodoSetPlaceholder(string? value)
    {
        if (value == null)
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= TodoSetPlaceholderPrefix.Length ||
            !trimmed.StartsWith(TodoSetPlaceholderPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var previousWasUnderscore = true; // the prefix ends with '_'
        for (var i = TodoSetPlaceholderPrefix.Length; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            if (c == '_')
            {
                if (previousWasUnderscore)
                {
                    return false;
                }

                previousWasUnderscore = true;
                continue;
            }

            if (!((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
            {
                return false;
            }

            previousWasUnderscore = false;
        }

        return !previousWasUnderscore;
    }

    /// <summary>
    ///     True when a blueprint seed may carry <paramref name="value" /> for a Secret attribute: only
    ///     <c>null</c> or an empty or whitespace-only string (decisions 2026-10-06, item 1). A placeholder
    ///     (<c>&lt;...&gt;</c>, <c>TODO_SET_...</c>) is a value and therefore not allowed.
    /// </summary>
    /// <param name="value">The seed value</param>
    /// <returns>True when the value is allowed in a seed</returns>
    public static bool IsAllowedSeedValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value);
    }
}
