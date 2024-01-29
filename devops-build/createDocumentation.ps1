param ($configuration = "DebugL")

dotnet tool install --global MMXMLDoc2Markdown --version 3.1.4

$modulePath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$baseBinPath = Resolve-Path(Join-Path $modulePath "../bin/$configuration/net8.0")
$baseDocsPath = Resolve-Path(Join-Path $modulePath "../docs")
$baseOutputPath = Join-Path $baseBinPath "documentation/"

# Clean directory
if (Test-Path -Path $baseOutputPath) {
    Remove-Item -Path $baseOutputPath -Recurse
}

# Copy all articles to output

Write-Host "Copy articles from '$baseDocsPath', doc is generated at '$baseOutputPath'"
Copy-Item -Path "$baseDocsPath" -Destination "$baseOutputPath/articles/construction-kit-engine" -Recurse

# Create XML documentation for Libraries
$outputPath = "$baseOutputPath/api/ConstructionKit.Contracts"
$sourcePath = "$baseBinPath/Meshmakers.Octo.ConstructionKit.Contracts.dll"
Write-Host "Creating documentation for $sourcePath, doc is generated at $outputPath"
mmxmldoc2md $sourcePath $outputPath

$outputPath = "$baseOutputPath/api/Runtime.Contracts"
$sourcePath = "$baseBinPath/Meshmakers.Octo.Runtime.Contracts.dll"
Write-Host "Creating documentation for $sourcePath, doc is generated at $outputPath"
mmxmldoc2md $sourcePath $outputPath
