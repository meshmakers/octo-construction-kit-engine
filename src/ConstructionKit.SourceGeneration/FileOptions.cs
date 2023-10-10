using Microsoft.CodeAnalysis.Diagnostics;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal readonly record struct FileOptions 
{
	public InnerClassVisibility InnerClassVisibility { get; init; }
	public GroupedModelFile GroupedFile { get; init; }
	public string? CustomToolNamespace { get; init; }
	public string LocalNamespace { get; init; }
	public string EmbeddedFilename { get; init; }
	public bool GenerateCkModelServiceClass { get; init; }
	public bool IsValid { get; init; }
	
	public FileOptions(
		GroupedModelFile groupedFile,
		AnalyzerConfigOptions options,
		GlobalOptions globalOptions
	)
	{
		var outputPath = Path.Combine(Path.GetDirectoryName(globalOptions.ProjectFullPath) ?? string.Empty, globalOptions.OutputPath);
		
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
		
		LocalNamespace =
			options.TryGetValue("build_metadata.EmbeddedResource.TargetPath", out var targetPath) &&
			targetPath is { Length: > 0 }
				? Utilities.GetLocalNamespace(
					resxFilePath, targetPath,
					globalOptions.ProjectFullPath,
					globalOptions.ProjectName,
					globalOptions.RootNamespace)
				: string.IsNullOrEmpty(detectedNamespace)
					? Utilities.SanitizeNamespace(globalOptions.ProjectName)
					: detectedNamespace;

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

		if (!resxFilePath.StartsWith(outputPath, StringComparison.OrdinalIgnoreCase))
		{
			IsValid = globalOptions.IsValid;
		}
	}

	public static FileOptions Select(
		GroupedModelFile file,
		AnalyzerConfigOptionsProvider options,
		GlobalOptions globalOptions
	)
	{
		return new FileOptions(
			groupedFile: file,
			options: options.GetOptions(file.MainFile.File),
			globalOptions: globalOptions
		);
	}
}