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
    ///     True when <paramref name="value" /> is a placeholder: a non-empty text enclosed in angle
    ///     brackets such as <c>&lt;set-after-install&gt;</c> or <c>&lt;FINAPI_CLIENT_SECRET&gt;</c>.
    ///     Blueprint seeds may only carry placeholders or empty values for Secret attributes
    ///     (decision 9); the write path stores a placeholder as "not set" (concept §3.6).
    /// </summary>
    /// <param name="value">The value to check</param>
    /// <returns>True for a placeholder</returns>
    public static bool IsPlaceholder(string? value)
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
    ///     True when a blueprint seed may carry <paramref name="value" /> for a Secret attribute:
    ///     <c>null</c>, an empty or whitespace-only string, or a placeholder
    ///     (<see cref="IsPlaceholder" />).
    /// </summary>
    /// <param name="value">The seed value</param>
    /// <returns>True when the value is allowed in a seed</returns>
    public static bool IsAllowedSeedValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || IsPlaceholder(value);
    }
}
