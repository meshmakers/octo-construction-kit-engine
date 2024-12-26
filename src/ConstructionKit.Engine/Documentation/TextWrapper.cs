namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class TextWrapper
{
    public static async Task AddDescription(StreamWriter outputFile, string description)
    {
        await outputFile.WriteLineAsync("").ConfigureAwait(false);
        await outputFile.WriteLineAsync($"{description}").ConfigureAwait(false);
    }
}