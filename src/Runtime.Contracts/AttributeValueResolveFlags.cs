namespace Meshmakers.Octo.Runtime.Contracts;

/// <summary>
/// Flags that define how attribute values should be resolved
/// </summary>
[Flags]
public enum AttributeValueResolveFlags
{
    /// <summary>
    /// Default behavior, no special resolution
    /// </summary>
    Default = 0,

    /// <summary>
    /// Resolves enum keys to their names
    /// </summary>
    ResolveEnumsToNames = 1,

    /// <summary>
    /// When a navigation step yields multiple candidate ends/targets (N-multiplicity
    /// associations), resolve deterministically to the first match ordered by RtId instead of
    /// throwing <c>MultipleNavigationEndsUnsupported</c>.
    /// </summary>
    FirstMatchNavigation = 2,
}