param ($configuration = "Release")

dotnet tool update --global MMXMLDoc2Markdown --version 3.1.7

$modulePath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$baseBinPath = Join-Path $modulePath "../bin/$configuration/net8.0/publish"
if (-not (Test-Path -Path $baseBinPath)) {
    throw "Bin path '$baseBinPath' does not exist"
}

$baseDocsPath = Resolve-Path(Join-Path $modulePath "../docs")
$baseOutputPath = Join-Path $baseBinPath "documentation"

# Clean directory
if (Test-Path -Path $baseOutputPath) {
    Write-Host "Remove existing documentation at '$baseOutputPath'"
    Remove-Item -Path $baseOutputPath -Recurse -Force
}

# Copy all developer guide articles to output
$outputPath = "$baseOutputPath/developerGuide/ConstructionKitEngine"
Write-Host "Copy articles from '$baseDocsPath', doc is generated at '$outputPath'"
Copy-Item -Path "$baseDocsPath/developerGuide" -Destination "$outputPath" -Recurse

# Create XML documentation for Libraries
$outputPath = "$baseOutputPath/apiReference/ConstructionKit.Contracts"
$sourcePath = "$baseBinPath/Meshmakers.Octo.ConstructionKit.Contracts.dll"
Write-Host "Creating documentation for $sourcePath, doc is generated at $outputPath"
mmxmldoc2md $sourcePath $outputPath

$outputPath = "$baseOutputPath/apiReference/Runtime.Contracts"
$sourcePath = "$baseBinPath/Meshmakers.Octo.Runtime.Contracts.dll"
Write-Host "Creating documentation for $sourcePath, doc is generated at $outputPath"
mmxmldoc2md $sourcePath $outputPath
