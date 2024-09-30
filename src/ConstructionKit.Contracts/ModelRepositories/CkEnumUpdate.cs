using Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

/// <summary>
/// Represents an update operation for an enum.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global
public class CkEnumUpdate
{
    /// <summary>
    /// The key of the enum value.
    /// </summary>
    public CkExtensionUpdateOperations Operation { get; set; }

    /// <summary>
    /// The value of the enum.
    /// </summary>
    public CkEnumValueDto Value { get; set; } = null!;
}