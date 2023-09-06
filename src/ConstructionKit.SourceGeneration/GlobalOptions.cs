using Microsoft.CodeAnalysis.Diagnostics;

namespace Meshmakers.Octo.ConstructionKit.SourceGeneration;

internal record GlobalOptions
{
    public string? RootNamespace { get; }
    public string ProjectFullPath { get; }
    public string ProjectName { get; }
    public bool IsValid { get; }
    
    public GlobalOptions(AnalyzerConfigOptions options)
    {
        IsValid = true;
        
        if (!options.TryGetValue("build_property.MSBuildProjectFullPath", out var projectFullPath))
        {
            IsValid = false;
        }
        ProjectFullPath = projectFullPath!;
        if (options.TryGetValue("build_property.RootNamespace", out var rootNamespace))
        {
            RootNamespace = rootNamespace;
        }
        if (!options.TryGetValue("build_property.MSBuildProjectName", out var projectName))
        {
            IsValid = false;
        }
        ProjectName = projectName!;
    }
    
    public static GlobalOptions Select(AnalyzerConfigOptionsProvider provider, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new GlobalOptions(provider.GlobalOptions);
    }
}