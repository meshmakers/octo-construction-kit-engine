namespace Meshmakers.Octo.ConstructionKit.Contracts.DataTransferObjects;

/// <summary>
/// Defines the state of the construction kit model
/// </summary>
public enum ModelState
{
    /// <summary>
    /// The model is importing
    /// </summary>
    Importing = 0,
    
    /// <summary>
    /// The model is available
    /// </summary>
    Available = 1,

    /// <summary>
    /// The model failed to resolve because of missing dependencies
    /// </summary>
    ResolveFailed = 2
}