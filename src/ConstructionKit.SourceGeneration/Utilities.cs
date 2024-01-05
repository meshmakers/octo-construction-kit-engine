using System.Text.RegularExpressions;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal static class Utilities
{
    public static string GetClassNameFromPath(string resxFilePath)
    {
        // Fix issues with files that have names like xxx.aspx.resx
        var className = resxFilePath;
        while (className.Contains("."))
        {
            className = Path.GetFileNameWithoutExtension(className);
        }

        return className;
    }

    public static string GetLocalNamespace(
        string? resxPath,
        string? targetPath,
        string projectPath,
        string projectName,
        string? rootNamespace
    )
    {
        try
        {
            if (resxPath is null)
            {
                return string.Empty;
            }

            var resxFolder = Path.GetDirectoryName(resxPath);
            var projectFolder = Path.GetDirectoryName(projectPath);
            rootNamespace ??= string.Empty;

            if (resxFolder is null || projectFolder is null)
            {
                return string.Empty;
            }

            var localNamespace = string.Empty;

            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                localNamespace = Path.GetDirectoryName(targetPath)?
                    .Trim(Path.DirectorySeparatorChar)
                    .Trim(Path.AltDirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '.')
                    .Replace(Path.AltDirectorySeparatorChar, '.');
            }
            else if (resxFolder.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase))
            {
                localNamespace = resxFolder
                    .Substring(projectFolder.Length)
                    .Trim(Path.DirectorySeparatorChar)
                    .Trim(Path.AltDirectorySeparatorChar)
                    .Replace(Path.DirectorySeparatorChar, '.')
                    .Replace(Path.AltDirectorySeparatorChar, '.');
            }

            if (string.IsNullOrEmpty(rootNamespace) && string.IsNullOrEmpty(localNamespace))
            {
                // If local namespace is empty, e.g file is in root project folder, root namespace set to empty
                // fallback to project name as a namespace
                localNamespace = SanitizeNamespace(projectName);
            }
            else
            {
                localNamespace = (string.IsNullOrEmpty(localNamespace) || localNamespace == null
                        ? rootNamespace
                        : $"{rootNamespace}.{SanitizeNamespace(localNamespace, false)}")
                    .Trim('.');
            }

            return localNamespace;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    public static string SanitizeNamespace(string ns, bool sanitizeFirstChar = true)
    {
        if (string.IsNullOrEmpty(ns))
        {
            return ns;
        }

        // A namespace must contain only alphabetic characters, decimal digits, dots and underscores, and must begin with an alphabetic character or underscore (_)
        // In case there are invalid chars we'll use same logic as Visual Studio and replace them with underscore (_) and append underscore (_) if project does not start with alphabetic or underscore (_)

        var sanitizedNs = Regex
            .Replace(ns, @"[^a-zA-Z0-9_\.]", "_");

        // Handle folder containing multiple dots, e.g. 'test..test2' or starting, ending with dots
        sanitizedNs = Regex
            .Replace(sanitizedNs, @"\.+", ".");

        if (sanitizeFirstChar)
        {
            sanitizedNs = sanitizedNs.Trim('.');
        }

        return sanitizeFirstChar
            // Handle namespace starting with digit
            ? char.IsDigit(sanitizedNs[0]) ? $"_{sanitizedNs}" : sanitizedNs
            : sanitizedNs;
    }
}