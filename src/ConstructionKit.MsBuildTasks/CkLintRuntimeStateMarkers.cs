using Microsoft.Build.Framework;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.ConstructionKit.MsBuildTasks;

/// <summary>
///     MSBuild lint task that fails the build when a CK attribute YAML declaration takes no
///     explicit position on ownership. Authors must answer the question at attribute creation
///     time: values the tenant owns are preserved on blueprint re-apply, values the seed owns are
///     overwritten. Forgetting the marker silently defaults the attribute to seed-managed and a
///     future blueprint version bump wipes the tenant's value (regression history in
///     <c>octo-communication-controller-services/docs/runbooks/recover-mesh-adapter-state.md</c>
///     and CK engine <c>CLAUDE.md</c> "Runtime-State Preservation on Re-Apply").
/// </summary>
/// <remarks>
///     <para>
///         A declaration satisfies the lint with EITHER the current
///         <c>ownership: SeedOwned | TenantOwned | RuntimeState | Secret</c> (AB#5187) OR the
///         deprecated <c>isRuntimeState: true|false</c> alias, so a project that swept its
///         markers before ownership existed stays green and can migrate file by file. Declaring
///         both on one attribute is rejected (OCTO-CK003): the engine would resolve it
///         deterministically — <c>ownership</c> wins — but a reader cannot tell which one is
///         meant, and a stale alias that contradicts the enum is exactly how a credential ends up
///         unprotected.
///     </para>
/// </remarks>
/// <remarks>
///     <para>
///         The task is opt-in per project via the <c>OctoEnforceRuntimeStateMarkers</c>
///         MSBuild property (default <c>false</c>). Existing CK model projects that pre-date
///         the marker can stay on the default until each owner does the marker sweep; once a
///         project flips the property to <c>true</c>, any subsequent attribute that lacks the
///         marker fails the build with a clear pointer to the offending YAML line.
///     </para>
///     <para>
///         Scanning is folder-local: every <c>attributes/*.yaml</c> file under each declared
///         <c>&lt;ConstructionKitFolder&gt;</c> is parsed; the task does not chase CK dependencies
///         into NuGet-imported models. The author has no leverage over imported attributes
///         anyway; they were vetted by the model they came from.
///     </para>
/// </remarks>
public class CkLintRuntimeStateMarkers : Microsoft.Build.Utilities.Task
{
    /// <summary>
    ///     Mirror of <c>AttributeOwnershipDto</c>. Duplicated as strings on purpose: the task
    ///     assembly is multi-targeted to <c>netstandard2.0</c> for the legacy MSBuild host and
    ///     deliberately does not reference the engine contracts (see the local YAML shapes below).
    ///     A value added there without being added here fails the lint loudly (OCTO-CK004) rather
    ///     than silently passing.
    /// </summary>
    private static readonly string[] ValidOwnershipValues = ["SeedOwned", "TenantOwned", "RuntimeState", "Secret"];

    /// <summary>
    ///     Root folders to scan for an <c>attributes</c> subdirectory. Each <c>%(Identity)</c>
    ///     points at a CK model's root folder (the same convention <c>CkCompile</c> uses).
    /// </summary>
    [Required]
    public ITaskItem[] ConstructionKitFolders { get; set; } = null!;

    /// <inheritdoc />
    public override bool Execute()
    {
        var yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var attributeFileCount = 0;
        var attributeCount = 0;
        var missingMarkerCount = 0;
        var hasErrors = false;

        foreach (var folderItem in ConstructionKitFolders)
        {
            var folderPath = folderItem.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                continue;
            }

            var attributesFolder = Path.Combine(folderPath, "attributes");
            if (!Directory.Exists(attributesFolder))
            {
                continue;
            }

            foreach (var yamlFile in Directory.GetFiles(attributesFolder, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                attributeFileCount++;

                AttributeFileShape? attributeFile;
                try
                {
                    var content = File.ReadAllText(yamlFile);
                    attributeFile = yamlDeserializer.Deserialize<AttributeFileShape>(content);
                }
                catch (Exception ex)
                {
                    Log.LogError(
                        subcategory: null,
                        errorCode: "OCTO-CK002",
                        helpKeyword: null,
                        file: yamlFile,
                        lineNumber: 0,
                        columnNumber: 0,
                        endLineNumber: 0,
                        endColumnNumber: 0,
                        message: "Failed to parse CK attribute file: {0}",
                        messageArgs: ex.Message);
                    hasErrors = true;
                    continue;
                }

                if (attributeFile?.Attributes == null)
                {
                    continue;
                }

                foreach (var attribute in attributeFile.Attributes)
                {
                    attributeCount++;

                    var attributeId = attribute.Id ?? "<unknown>";
                    var hasOwnership = !string.IsNullOrWhiteSpace(attribute.Ownership);

                    if (hasOwnership && !ValidOwnershipValues.Contains(attribute.Ownership!.Trim()))
                    {
                        LogAttributeError(yamlFile, "OCTO-CK004",
                            "CK attribute '{0}' declares an unknown ownership value '{1}'. Valid values are: {2}.",
                            attributeId, attribute.Ownership!.Trim(), string.Join(", ", ValidOwnershipValues));
                        hasErrors = true;
                        continue;
                    }

                    if (hasOwnership && attribute.IsRuntimeState != null)
                    {
                        LogAttributeError(yamlFile, "OCTO-CK003",
                            "CK attribute '{0}' declares both 'ownership' and the deprecated 'isRuntimeState' marker. Keep 'ownership' and remove 'isRuntimeState' — the engine resolves the conflict in favour of 'ownership', so leaving the alias behind only hides what the attribute actually does.",
                            attributeId);
                        hasErrors = true;
                        continue;
                    }

                    if (!hasOwnership && attribute.IsRuntimeState == null)
                    {
                        LogAttributeError(yamlFile, "OCTO-CK001",
                            "CK attribute '{0}' is missing the required 'ownership' marker. Declare one of: 'ownership: Secret' for credentials, tokens, keys and passwords; 'ownership: RuntimeState' for values a service / operator / pipeline writes at runtime (status, counters, cursors, error history); 'ownership: TenantOwned' for values a tenant admin sets in the product and would be right to be angry about losing (tariffs, IBAN, market ids, mailbox/host/port, branding) — preserved on re-apply AND still exported; 'ownership: SeedOwned' for values that ship with the product and a new version must be able to correct (endpoints, report template names, statutory defaults, taxonomy). The deprecated 'isRuntimeState: true|false' is still accepted.",
                            attributeId);
                        missingMarkerCount++;
                        hasErrors = true;
                    }
                }
            }
        }

        Log.LogMessage(MessageImportance.Normal,
            "CkLintRuntimeStateMarkers: scanned {0} attribute file(s) / {1} attribute(s); {2} missing ownership marker(s).",
            attributeFileCount, attributeCount, missingMarkerCount);

        return !hasErrors;
    }

    private void LogAttributeError(string yamlFile, string errorCode, string message, params object[] messageArgs)
    {
        Log.LogError(
            subcategory: null,
            errorCode: errorCode,
            helpKeyword: null,
            file: yamlFile,
            lineNumber: 0,
            columnNumber: 0,
            endLineNumber: 0,
            endColumnNumber: 0,
            message: message,
            messageArgs: messageArgs);
    }

    /// <summary>
    ///     Local YAML deserialization shape — kept independent of the <c>CkAttributeDto</c>
    ///     contract so the task does not have to reference the engine project (the build-task
    ///     assembly is multi-targeted to <c>netstandard2.0</c> for the legacy MSBuild host;
    ///     pulling in the engine contracts would inflate the package and tighten the target
    ///     frameworks).
    /// </summary>
    private sealed class AttributeFileShape
    {
        public List<AttributeEntryShape>? Attributes { get; set; }
    }

    private sealed class AttributeEntryShape
    {
        public string? Id { get; set; }

        /// <summary>
        ///     Deprecated alias for <see cref="Ownership" />. Nullable so "absent" is
        ///     distinguishable from an explicit <c>false</c>.
        /// </summary>
        public bool? IsRuntimeState { get; set; }

        /// <summary>
        ///     Read as a raw string rather than an enum so an unknown value produces a targeted
        ///     lint error (OCTO-CK004) instead of a YAML deserialisation failure that points at
        ///     the file but not at the attribute.
        /// </summary>
        public string? Ownership { get; set; }
    }
}
