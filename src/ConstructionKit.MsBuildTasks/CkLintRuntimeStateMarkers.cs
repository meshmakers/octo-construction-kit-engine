using Microsoft.Build.Framework;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Meshmakers.Octo.ConstructionKit.MsBuildTasks;

/// <summary>
///     MSBuild lint task that fails the build when a CK attribute YAML declaration does not
///     explicitly set <c>isRuntimeState</c> (either <c>true</c> or <c>false</c>). Authors
///     must take a position at attribute creation time: runtime-state attributes are preserved
///     on blueprint re-apply, configuration attributes are overwritten. Forgetting the marker
///     silently defaults the attribute to seed-managed and a future blueprint version bump
///     wipes any runtime value (regression history in
///     <c>octo-communication-controller-services/docs/runbooks/recover-mesh-adapter-state.md</c>
///     and CK engine <c>CLAUDE.md</c> "Runtime-State Preservation on Re-Apply").
/// </summary>
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

                    if (attribute.IsRuntimeState == null)
                    {
                        Log.LogError(
                            subcategory: null,
                            errorCode: "OCTO-CK001",
                            helpKeyword: null,
                            file: yamlFile,
                            lineNumber: 0,
                            columnNumber: 0,
                            endLineNumber: 0,
                            endColumnNumber: 0,
                            message:
                            "CK attribute '{0}' is missing the required 'isRuntimeState' marker. Declare 'isRuntimeState: true' for runtime state owned by services / operators / users at runtime (status, counters, error history — preserved on blueprint re-apply) or 'isRuntimeState: false' for blueprint-author-owned configuration (seed wins on re-apply).",
                            messageArgs: attribute.Id ?? "<unknown>");
                        missingMarkerCount++;
                        hasErrors = true;
                    }
                }
            }
        }

        Log.LogMessage(MessageImportance.Normal,
            "CkLintRuntimeStateMarkers: scanned {0} attribute file(s) / {1} attribute(s); {2} missing 'isRuntimeState' marker(s).",
            attributeFileCount, attributeCount, missingMarkerCount);

        return !hasErrors;
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

        public bool? IsRuntimeState { get; set; }
    }
}
