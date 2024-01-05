namespace Meshmakers.Octo.Runtime.Engine.Configuration;

/// <summary>
///     Represents the options for a local repository
/// </summary>
public class LocalRuntimeRepositoryConfiguration
{
    /// <summary>
    ///     The tenant name
    /// </summary>
    public string TenantId { get; set; } = null!;

    /// <summary>
    ///     The path the local runtime repository get stored
    /// </summary>
    public string DirectoryPath { get; set; } = null!;
}