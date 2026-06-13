param ($configuration = "Release", $frameworkVersion = "net9.0")

# Install/update the API doc generator (provides the 'mmxmldoc2md' command used below).
dotnet tool update --global Meshmakers.XMLDoc2Markdown

$modulePath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$baseBinPath = Join-Path $modulePath "../bin/$configuration/$frameworkVersion"
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
mmxmldoc2md $sourcePath $outputPath

$outputPath = "$baseOutputPath/apiReference/Runtime.Contracts"
$sourcePath = "$baseBinPath/Meshmakers.Octo.Runtime.Contracts.dll"
Write-Host "Creating documentation for $sourcePath, doc is generated at $outputPath"
mmxmldoc2md $sourcePath $outputPath