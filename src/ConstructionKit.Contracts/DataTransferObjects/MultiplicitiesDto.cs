namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
///     Defines multiplicities for ck associations.
/// </summary>
public enum MultiplicitiesDto
{
    /// <summary>
    ///     Multiplicity zero or one.
    /// </summary>
    ZeroOrOne = 0,

    /// <summary>
    ///     Multiplicity one.
    /// </summary>
    One = 1,

    /// <summary>
    ///     Multiplicity more than one.
    /// </summary>
    N = 2
}