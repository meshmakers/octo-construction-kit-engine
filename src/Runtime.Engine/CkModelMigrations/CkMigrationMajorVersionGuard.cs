using Meshmakers.Octo.ConstructionKit.Contracts;

namespace Meshmakers.Octo.Runtime.Engine.CkModelMigrations;

/// <summary>
///     Refuses schema-only bridges that would carry a tenant across a MAJOR version of a CK model.
/// </summary>
/// <remarks>
///     <para>
///         The migration path finder synthesises no-op ("schema-only") steps so that additive minor/patch
///         bumps do not need a tombstone migration script. That shortcut is only sound inside one major
///         line: a major bump is, by the CK semver rules, the one bump that is allowed to rename or remove
///         things, so "nothing to migrate" can no longer be assumed.
///     </para>
///     <para>
///         🔴 The failure this guards against is silent (AB#4924, G3): System.Communication 4.0.0 renames
///         Pool to DeploymentSite, but its migration-meta only listed entry points up to 3.36.0. A tenant
///         on 3.40.0 found no entry, fell through to the post-chain schema-only bridge, and was lifted to
///         4.x WITHOUT the rename — Pool entities of a type the model no longer defines, hosting edges with
///         a role nothing declares, and no error anywhere.
///     </para>
///     <para>
///         The guard applies to the tail of a path only — the post-chain bridge, and the schema-only end
///         gap of a partial path (chain reaches a version below the target). The start-gap bridge in front
///         of a migration chain is unaffected: there the chain itself carries the data across the major.
///         A model that ships no migration-meta at all is unaffected too (it declares that it has nothing
///         to migrate).
///     </para>
/// </remarks>
internal static class CkMigrationMajorVersionGuard
{
    /// <summary>
    ///     True when going from <paramref name="from" /> to <paramref name="to" /> is an upgrade to a higher
    ///     major version.
    /// </summary>
    public static bool CrossesMajor(CkVersion from, CkVersion to) => from.Major < to.Major;

    /// <summary>
    ///     The error text for a refused bridge. One wording for the log, the migration result and the
    ///     upgrade result, so an operator can grep for it.
    /// </summary>
    /// <param name="modelName">The CK model name</param>
    /// <param name="installedVersion">The version the tenant is on</param>
    /// <param name="targetVersion">The version that was requested</param>
    /// <param name="chainEndVersion">
    ///     When a migration chain was found but ends below the target in a lower major, the version it
    ///     reaches; <c>null</c> when no chain applies at all.
    /// </param>
    public static string BuildRefusalMessage(string modelName, CkVersion installedVersion, CkVersion targetVersion,
        CkVersion? chainEndVersion = null)
    {
        var gap = chainEndVersion is { } end
            ? $"the migration chain ends at {end}, below major {targetVersion.Major}"
            : $"no migration entry in the migration-meta of {modelName}-{targetVersion} starts at or above {installedVersion}";

        return $"No migration path found from {modelName}-{installedVersion} to {modelName}-{targetVersion}: " +
               $"the upgrade crosses a major version ({installedVersion.Major}.x -> {targetVersion.Major}.x) and {gap}. " +
               "A schema-only bridge across a major version is refused because it would skip the data migration. " +
               $"The data stays at {installedVersion}; add a migration entry for {installedVersion} " +
               "(a script without steps if this major bump really needs no data migration) and re-run the import.";
    }
}
