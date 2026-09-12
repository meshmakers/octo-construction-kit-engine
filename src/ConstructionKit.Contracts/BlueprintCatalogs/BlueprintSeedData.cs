using Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs.DataTransferObjects;

namespace Meshmakers.Octo.ConstructionKit.Contracts.BlueprintCatalogs;

/// <summary>
///     Resolves the seed-data files a blueprint declares. A blueprint may either name a single file
///     in <see cref="BlueprintMetaRootDto.SeedDataPath" /> (the original form) or split its seed
///     across several files and folders via <see cref="BlueprintMetaRootDto.SeedDataPaths" />
///     (AB#4758). Both the runtime (<c>BlueprintService</c>) and the authoring tooling
///     (<c>BlueprintCompilerService</c> / octo-bpm) go through this helper so they can never
///     disagree about which files make up a blueprint's seed.
/// </summary>
public static class BlueprintSeedData
{
    /// <summary>
    ///     Returns the effective, de-duplicated list of seed-data file paths of a blueprint, in load
    ///     order: the single <see cref="BlueprintMetaRootDto.SeedDataPath" /> first (when set),
    ///     followed by <see cref="BlueprintMetaRootDto.SeedDataPaths" />. Paths are returned in their
    ///     normalised forward-slash form. Blank entries are dropped; a blueprint without any seed
    ///     data yields an empty list.
    /// </summary>
    /// <remarks>
    ///     Both properties are honoured rather than treating them as mutually exclusive: silently
    ///     ignoring one of them would drop entities from the install without any signal.
    ///     <c>BlueprintCompilerService.ValidateAsync</c> warns when a blueprint uses both forms.
    /// </remarks>
    public static IReadOnlyList<string> ResolvePaths(BlueprintMetaRootDto blueprintMeta)
    {
        if (blueprintMeta == null)
        {
            throw new ArgumentNullException(nameof(blueprintMeta));
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalised = Normalise(path!);
            if (normalised.Length > 0 && seen.Add(normalised))
            {
                result.Add(normalised);
            }
        }

        Add(blueprintMeta.SeedDataPath);

        if (blueprintMeta.SeedDataPaths != null)
        {
            foreach (var path in blueprintMeta.SeedDataPaths)
            {
                Add(path);
            }
        }

        return result;
    }

    /// <summary>
    ///     Normalises a blueprint-relative path to the forward-slash form used as the key for
    ///     de-duplication and for the catalog lookup. This deliberately does not reject traversal
    ///     paths — that is <see cref="BlueprintRelativePath.Validate" />'s job at open time.
    /// </summary>
    public static string Normalise(string path)
    {
        return path.Replace('\\', '/').Trim().TrimStart('/');
    }
}
