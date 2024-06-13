namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class TextWrapper
{
    public static async Task AddDescription(StreamWriter outputFile, string description)
    {
        await outputFile.WriteLineAsync("<details>").ConfigureAwait(false);
        await outputFile.WriteLineAsync("<summary>Description</summary>").ConfigureAwait(false);
        await outputFile.WriteLineAsync($"<div>{description}</div>").ConfigureAwait(false);
        await outputFile.WriteLineAsync("</details>").ConfigureAwait(false);
    }
}