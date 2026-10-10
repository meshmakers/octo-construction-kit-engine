using System.Text.RegularExpressions;
using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.ConstructionKit.Engine.SemVer;

/// <summary>
///     Rewrites the version of the <c>modelId</c> line of a <c>ckModel.yaml</c> text and nothing else (AB#4467):
///     comments, key order, quoting, indentation and line endings stay byte-identical. Pure string transformation.
/// </summary>
public static class CkModelVersionWriter
{
    /// <summary>
    ///     Replaces the version in the single <c>modelId: Name-X.Y.Z</c> line of <paramref name="yaml" />.
    /// </summary>
    /// <param name="yaml">The text of <c>ckModel.yaml</c>.</param>
    /// <param name="modelName">The model name the line has to carry (guards against editing the wrong line).</param>
    /// <param name="newVersion">The version to write.</param>
    /// <param name="updated">The updated text; the input when this method returns false.</param>
    /// <returns>True when exactly one <c>modelId</c> line of the model was found and its version replaced.</returns>
    public static bool TryReplaceVersion(string yaml, string modelName, CkVersion newVersion, out string updated)
    {
        var pattern = new Regex(
            @"^(?<head>[ \t]*modelId[ \t]*:[ \t]*[""']?" + Regex.Escape(modelName) + @"-)(?<version>\d+(?:\.\d+){0,2})(?<tail>[""']?[ \t]*(?:#.*)?)\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        var matches = pattern.Matches(yaml);
        if (matches.Count != 1)
        {
            updated = yaml;
            return false;
        }

        var version = matches[0].Groups["version"];
        updated = yaml.Substring(0, version.Index) + newVersion + yaml.Substring(version.Index + version.Length);
        return true;
    }
}
