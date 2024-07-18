param ($configuration = "Release")

dotnet tool update --global MMXMLDoc2Markdown

$modulePath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$baseBinPath = Join-Path $modulePath "../bin/$configuration/net8.0"
if (-not (Test-Path -Path $baseBinPath)) {
    throw "Bin path '$baseBinPath' does not exist"
}

$baseOutputPath = Join-Path $baseBinPath "documentation"

# Clean directory
if (Test-Path -Path $baseOutputPath) {
    Write-Host "Remove existing documentation at '$baseOutputPath'"
    Remove-Item -Path $baseOutputPath -Recurse -Force
}

# Create XML documentation for Libraries
$outputPath = "$baseOutputPath/apiReference/ConstructionKit.Contracts"
$sourcePath = "$baseBinPath/Meshmakers.Octo.ConstructionKit.Contracts.dll"
Write-Host "Creating documentation for $sourcePath, doc is generated at $outputPath"
mmxmldoc2md $sourcePath $outputPath --github-pages

$outputPath = "$baseOutputPath/apiReference/Runtime.Contracts"
$sourcePath = "$baseBinPath/Meshmakers.Octo.Runtime.Contracts.dll"
Write-Host "Creating documentation for $sourcePath, doc is generated at $outputPath"
mmxmldoc2md $sourcePath $outputPath --github-pages

$projectPath = "$baseBinPath/Meshmakers.Octo.ConstructionKit.Compiler"

function callCompilerCommand {
    param (
        [string]$commandName,
        [string]$sourcePath,
        [string]$outputPath,
        [string]$version,
        [string]$linkPath
    )
    
    Write-Host "Generating docs with the following parameters:"
    Write-Host "Command Name: $commandName"
    Write-Host "Source Path: $sourcePath"
    Write-Host "Output Path: $outputPath"
    Write-Host "Version: $version"
    Write-Host "Link Path: $linkPath"
    
    # Call the specified command from the installed tool
    & dotnet run --project $projectPath -- -c $commandName -f $sourcePath -o $outputPath -v $version -l $linkPath
}

# Calls the callCompilerCommand with the specified parameters
$commandName = "generateDocs"
# intended path?
$sourcePath = Join-Path $modulePath "../../octo-construction-kit/src/constructionKits/Octo.Sdk.Packages.Basic/ConstructionKit/ckModel.yaml"
$outputPath = "$baseOutputPath/technologyGuide/constructionKits"
$version = "1.0"
$linkPath = "/docs/technologyGuide/constructionKits/"
callCompilerCommand -commandName $commandName -sourcePath $sourcePath -outputPath $outputPath -version $version -linkPath $linkPath