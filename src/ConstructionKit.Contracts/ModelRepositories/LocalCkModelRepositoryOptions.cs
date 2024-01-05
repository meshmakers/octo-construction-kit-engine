// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace Meshmakers.Octo.ConstructionKit.Contracts.ModelRepositories;

/// <summary>
///     Options for the local CK model repository
/// </summary>
public class LocalCkModelRepositoryOptions
{
    /// <summary>
    ///     Creates a new instance of <see cref="LocalCkModelRepositoryOptions" />
    /// </summary>
    public LocalCkModelRepositoryOptions()
    {
        RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".octo-ck-models");
    }

    /// <summary>
    ///     The local path where the CK models are stored
    /// </summary>
    public string RootPath { get; set; }
}