namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

/// <summary>
/// Allows for Selection of Documentation Mode (For Docusaurus use) and Diagram Mode (For Angular/ASP .Net Use)
/// </summary>
public class ModeSelectionOptions
{
    /// <summary>
    /// Name of Selection
    /// </summary>
    public const string ModeSelection = "ModeSelection";
    
    /// <summary>
    /// Mode of Choice true = full documentation, false = diagram only
    /// </summary>
    public bool DocumentationMode { get; set; } = true;
}