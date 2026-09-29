using Meshmakers.Octo.ConstructionKit.Contracts.ModelCatalogs;

namespace Meshmakers.Octo.ConstructionKit.MsBuildTasks;

/// <summary>
///     Applies the optional lane-scoped repository coordinates of the PRIVATE GitHub catalog to its
///     options, leaving every coordinate that carries no value at its compiled-in default.
/// </summary>
/// <remarks>
///     AB#5412 (Phase 2 of AB#5139): the private catalog slot is lane-scoped — the 0.2-dev lane
///     resolves and publishes through <c>meshmakers/octo-catalog-dev</c> while main and r-tags keep
///     <c>construction-kit-libraries-build</c>. The CLI tools have been able to retarget it for a long
///     time (<c>octo-ckc -c Config -go/-gr/-gb/-gp</c>), the MSBuild tasks could not — so a lane bump
///     of an embedded platform model (System.Communication 4.x) stayed invisible to every other model
///     on the lane and its dependents died with OCTO-CK103 no matter what they committed.
///     <para>
///         Values are applied VERBATIM. The CLI's <c>Config</c> command lower-cases them, which is
///         safe for GitHub owner/repo but not for the Pages URI: that is a URL path, and folding its
///         case is not this code's decision. The values come from a pipeline template, not from user
///         input.
///     </para>
/// </remarks>
internal static class PrivateGitHubCatalogCoordinates
{
    /// <summary>
    ///     Rejects a coordinate that still carries an unexpanded pipeline macro, e.g. the literal
    ///     <c>$(OctoPrivateGitHubCatalogOwner)</c> that an agent leaves behind when a build step
    ///     references a variable the pipeline never set.
    /// </summary>
    /// <remarks>
    ///     Without this guard such a value would be taken at face value and the model would be
    ///     resolved from — or worse, PUBLISHED to — a repository named after the macro, which fails as
    ///     a puzzling 404 far away from its cause. It is a realistic state: the consuming repo passes
    ///     the four <c>/p:</c> switches and gets the values from a template tag, so pinning the repo
    ///     back to an older tag while keeping the switches produces exactly this.
    /// </remarks>
    public static bool Validate(string? owner, string? repositoryName, string? branch, string? pagesUri,
        out string? error)
    {
        var candidates = new[]
        {
            ("owner", owner), ("repositoryName", repositoryName), ("branch", branch), ("pagesUri", pagesUri)
        };

        foreach (var candidate in candidates)
        {
            if (candidate.Item2 != null && candidate.Item2.Contains("$("))
            {
                error =
                    $"Private GitHub catalog coordinate '{candidate.Item1}' carries an unexpanded pipeline " +
                    $"macro ('{candidate.Item2}'). Either the pipeline does not set " +
                    "OctoPrivateGitHubCatalog* (template tag too old for the /p: switches that reference " +
                    "them) or the name is misspelled. Refusing to resolve or publish against a catalog " +
                    "named after a macro.";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    ///     Overwrites the coordinates that carry a value; the others keep their compiled-in default.
    ///     Call <see cref="Validate" /> first — this method trusts its input.
    /// </summary>
    public static void Apply(GitHubCatalogOptions options, string? owner, string? repositoryName,
        string? branch, string? pagesUri)
    {
        if (!string.IsNullOrWhiteSpace(owner))
        {
            options.GitHubRepositoryOwner = owner!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(repositoryName))
        {
            options.GitHubRepositoryName = repositoryName!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(branch))
        {
            options.GitHubRepositoryBranch = branch!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(pagesUri))
        {
            options.GitHubPagesUri = pagesUri!.Trim();
        }

    }

    /// <summary>
    ///     Renders the EFFECTIVE coordinates as <c>owner/repo@branch (pages: uri)</c> — the one line a
    ///     build log needs to prove which catalog a model was resolved from and published to, now that
    ///     the name <c>PrivateGitHubCatalog</c> no longer identifies a repository on its own.
    /// </summary>
    public static string Describe(GitHubCatalogOptions options)
    {
        return
            $"{options.GitHubRepositoryOwner}/{options.GitHubRepositoryName}@{options.GitHubRepositoryBranch} " +
            $"(pages: {options.GitHubPagesUri})";
    }
}
