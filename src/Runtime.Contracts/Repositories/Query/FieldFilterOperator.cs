namespace Meshmakers.Octo.Runtime.Contracts.Repositories.Query;

/// <summary>
/// Represents the operator to use for a field filter
/// </summary>
public enum FieldFilterOperator
{
    /// <summary>
    /// Equals
    /// </summary>
    Equals = 0,
    
    /// <summary>
    /// Not equals
    /// </summary>
    NotEquals = 1,
    
    /// <summary>
    /// Less than
    /// </summary>
    LessThan = 2,
    
    /// <summary>
    /// Less or equal than
    /// </summary>
    LessEqualThan = 3,
    
    /// <summary>
    /// Greater than
    /// </summary>
    GreaterThan = 4,
    
    /// <summary>
    /// Greater or equal than
    /// </summary>
    GreaterEqualThan = 5,
    
    /// <summary>
    /// In
    /// </summary>
    In = 6,
    
    /// <summary>
    /// Not in
    /// </summary>
    NotIn = 7,
    
    /// <summary>
    /// Like (string comparison)
    /// </summary>
    Like = 8,
    
    /// <summary>
    /// Regular expression match (string comparison)
    /// </summary>
    MatchRegEx = 9,
   
    /// <summary>
    /// Arrays: Any element equals
    /// </summary>
    AnyEq = 10
}
