namespace Meshmakers.Octo.ConstructionKit.Engine.BlueprintCatalogs;

/// <summary>
///     The stable codes of the blueprint draft findings (construction kit message catalog). Tools branch on
///     these numbers, never on the text.
/// </summary>
public static class BlueprintDraftValidationCodes
{
    /// <summary>blueprint.yaml missing.</summary>
    public const int ManifestMissing = 149;
    /// <summary>blueprint.yaml cannot be parsed.</summary>
    public const int ManifestUnreadable = 150;
    /// <summary>Blueprint id missing.</summary>
    public const int BlueprintIdMissing = 151;
    /// <summary>The single seed file is missing (warning).</summary>
    public const int SeedDataFileMissing = 152;
    /// <summary>One of several seed files is missing.</summary>
    public const int SeedDataFileOfSeveralMissing = 153;
    /// <summary>Both seedDataPath and seedDataPaths declared (warning).</summary>
    public const int SeedDataPathAndPathsDeclared = 154;
    /// <summary>A seed file cannot be parsed.</summary>
    public const int SeedDataFileUnparsable = 155;
    /// <summary>Duplicate entity in one seed file (warning).</summary>
    public const int SeedEntityDuplicateInFile = 156;
    /// <summary>Duplicate entity across seed files.</summary>
    public const int SeedEntityDuplicateAcrossFiles = 157;
    /// <summary>Seed file not referenced (warning).</summary>
    public const int SeedDataFileUnreferenced = 158;
    /// <summary>rtId reused under another CK type (warning).</summary>
    public const int SeedRtIdReusedAcrossTypes = 159;
    /// <summary>Seed sets a Secret attribute.</summary>
    public const int SeedSecretValue = BlueprintSeedSecretLint.SecretSeedValueMessageNumber;
    /// <summary>Secret lint could not resolve a CK model (warning).</summary>
    public const int SecretLintModelUnresolved = BlueprintSeedSecretLint.UnresolvedModelMessageNumber;
    /// <summary>CK model dependency without a name.</summary>
    public const int CkModelDependencyNameMissing = 162;
    /// <summary>CK model dependency range resolves in no catalog.</summary>
    public const int CkModelDependencyNotInCatalogs = 163;
    /// <summary>CK model dependency range without a minimum version.</summary>
    public const int CkModelDependencyNoMinimumVersion = 164;
    /// <summary>CK model dependency ranges not checked (info).</summary>
    public const int CkModelDependenciesNotChecked = 165;
    /// <summary>A CK model dependency could not be checked (warning).</summary>
    public const int CkModelDependencyNotCheckable = 166;
    /// <summary>The draft (zip) cannot be read.</summary>
    public const int DraftUnreadable = 167;

    /// <summary>
    ///     Codes of findings that make an install fail or leave a half-initialised tenant anyway: the install path
    ///     refuses on them before it changes anything. Every other error is reported as a warning by the install path -
    ///     also 163: the install step can still take a model from a service-managed descriptor or keep one the tenant
    ///     already runs, so it stays the authority there.
    /// </summary>
    public static readonly IReadOnlyCollection<int> InstallBlockingCodes = new HashSet<int>
    {
        ManifestMissing, ManifestUnreadable, BlueprintIdMissing, SeedDataFileOfSeveralMissing,
        SeedDataFileUnparsable, CkModelDependencyNameMissing, CkModelDependencyNoMinimumVersion
    };
}
