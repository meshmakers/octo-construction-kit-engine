namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
///     Represents the operator to use for a field filter
/// </summary>
public enum FieldFilterOperator
{
    /// <summary>
    ///     Equals for scalar values
    /// </summary>
    Equals = 0,

    /// <summary>
    ///     Not equals for scalar values
    /// </summary>
    NotEquals = 1,

    /// <summary>
    ///     Less than for scalar values
    /// </summary>
    LessThan = 2,

    /// <summary>
    ///     Less or equal than for scalar values
    /// </summary>
    LessEqualThan = 3,

    /// <summary>
    ///     Greater than for scalar values
    /// </summary>
    GreaterThan = 4,

    /// <summary>
    ///     Greater or equal than for scalar values
    /// </summary>
    GreaterEqualThan = 5,

    /// <summary>
    ///     Checks if the value is in a list of values
    /// </summary>
    In = 6,

    /// <summary>
    ///     Checks if the value is not in a list of values
    /// </summary>
    NotIn = 7,

    /// <summary>
    ///     Like (string comparison)
    /// </summary>
    Like = 8,

    /// <summary>
    ///     Regular expression match (string comparison)
    /// </summary>
    MatchRegEx = 9,

    /// <summary>
    ///   Scalar arrays: Check equality of any element to the comparison value
    /// </summary>
    AnyEq = 10,

    /// <summary>
    ///   Scalar strings: Check equality of any element to a string comparison value, e.g. *value*
    /// </summary>
    AnyLike = 11,

    /// <summary>
    ///     Matches documents that contain an array field with at least one element that matches all the specified query criteria.
    /// </summary>
    Match = 12,

    /// <summary>
    ///     Contains substring (string comparison)
    /// </summary>
    Contains = 13,

    /// <summary>
    ///     Starts with prefix (string comparison)
    /// </summary>
    StartsWith = 14,

    /// <summary>
    ///     Ends with suffix (string comparison)
    /// </summary>
    EndsWith = 15,

    /// <summary>
    ///     Between two values (inclusive)
    /// </summary>
    Between = 16,

    /// <summary>
    ///     Is null value
    /// </summary>
    IsNull = 17,

    /// <summary>
    ///     Is not null value
    /// </summary>
    IsNotNull = 18
}