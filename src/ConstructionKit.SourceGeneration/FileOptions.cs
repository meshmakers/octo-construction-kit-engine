using Microsoft.CodeAnalysis.Diagnostics;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal readonly record struct FileOptions
{
    public FileOptions(
        GroupedModelFile groupedFile,
        AnalyzerConfigOptions options,
        GlobalOptions globalOptions
    )
    {
        GroupedFile = groupedFile;
        var resxFilePath = groupedFile.MainFile.File.Path;

        var classNameFromFileName = Utilities.GetClassNameFromPath(resxFilePath);

        var detectedNamespace = Utilities.GetLocalNamespace(
            resxFilePath,
            options.TryGetValue("build_metadata.EmbeddedResource.Link", out var link) &&
            link is { Length: > 0 }
                ? link
                : null,
            globalOptions.ProjectFullPath,
            globalOptions.ProjectName,
            globalOptions.RootNamespace);

        EmbeddedFilename = string.IsNullOrEmpty(detectedNamespace) ? classNameFromFileName : $"{detectedNamespace}.{classNameFromFileName}";

        LocalNamespace = Utilities.SanitizeNamespace(globalOptions.RootNamespace ?? globalOptions.ProjectName);

        CustomToolNamespace =
            options.TryGetValue("build_metadata.EmbeddedResource.CustomToolNamespace", out var customToolNamespace) &&
            customToolNamespace is { Length: > 0 }
                ? customToolNamespace
                : null;

        if (
            options.TryGetValue("build_metadata.EmbeddedResource.InnerClassVisibility", out var innerClassVisibilitySwitch) &&
            Enum.TryParse(innerClassVisibilitySwitch, true, out InnerClassVisibility v) &&
            v != InnerClassVisibility.SameAsOuter
        )
        {
            InnerClassVisibility = v;
        }

        if (
            options.TryGetValue("build_metadata.EmbeddedResource.GenerateCkModelServiceClass", out var generateCkModelServiceClass) &&
            bool.TryParse(generateCkModelServiceClass, out var generate)
        )
        {
            GenerateCkModelServiceClass = generate;
        }

        IsValid = globalOptions.IsValid;
    }

    public InnerClassVisibility InnerClassVisibility { get; }
    public GroupedModelFile GroupedFile { get; }
    public string? CustomToolNamespace { get; }
    public string LocalNamespace { get; }
    public string EmbeddedFilename { get; }
    public bool GenerateCkModelServiceClass { get; }
    public bool IsValid { get; }

    public static FileOptions Select(
        GroupedModelFile file,
        AnalyzerConfigOptionsProvider options,
        GlobalOptions globalOptions
    )
    {
        return new FileOptions(
            file,
            options.GetOptions(file.MainFile.File),
            globalOptions
        );
    }
}