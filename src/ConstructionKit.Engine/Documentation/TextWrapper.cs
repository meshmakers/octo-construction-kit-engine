namespace Meshmakers.Octo.ConstructionKit.Engine.Documentation;

internal static class TextWrapper
{
    public static async Task AddDescription(StreamWriter outputFile, string description)
    {
        await outputFile.WriteLineAsync("").ConfigureAwait(false);
        // Escape curly braces to prevent MDX from interpreting them as JSX expressions
        var escapedDescription = EscapeMdxSpecialCharacters(description);
        await outputFile.WriteLineAsync(escapedDescription).ConfigureAwait(false);
    }

    /// <summary>
    /// Escapes special characters that would be interpreted by MDX as JSX expressions.
    /// Curly braces {} and angle brackets &lt;&gt; are escaped with backslashes to prevent
    /// acorn parser errors (e.g. literal placeholders like &lt;tenantId&gt; in URLs).
    /// </summary>
    public static string? EscapeMdxSpecialCharacters(string? text)
    {
        if (text == null || text.Length == 0)
        {
            return text;
        }

        return text.Replace("{", "\\{").Replace("}", "\\}").Replace("<", "\\<").Replace(">", "\\>");
    }
}