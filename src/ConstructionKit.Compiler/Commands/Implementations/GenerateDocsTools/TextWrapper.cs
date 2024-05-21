namespace Meshmakers.Octo.ConstructionKit.Compiler.Commands.Implementations.GenerateDocsTools;

internal static class TextWrapper
{
    public static async Task AddDescription(StreamWriter outputFile, string description)
    {
        //if description
        await outputFile.WriteLineAsync("<details>");
        await outputFile.WriteLineAsync("<summary>Description</summary>");
        await outputFile.WriteLineAsync($"<div>{description}</div>");
        await outputFile.WriteLineAsync("</details>");
    }
    
    //Add Mermaid Boilerplate Here
}